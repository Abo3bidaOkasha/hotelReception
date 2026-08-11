using NileJewal.Data;
using NileJewal.Models;
using NileJewal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace NileJewal.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IAuditService _auditService;

        public AdminController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IAuditService auditService)
        {
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
            _auditService = auditService;
        }

        // 1. التقرير الشهري واليومي (بناءً على تاريخ التحصيل والنقدية CreatedAt)
        public async Task<IActionResult> Dashboard(int? year, int? month, int? day)
        {
            int selectedYear = year ?? DateTime.Today.Year;
            int selectedMonth = month ?? DateTime.Today.Month;
            int selectedDay = day ?? DateTime.Today.Day;

            // التحقق من عدم تجاوز عدد أيام الشهر المختار
            int daysInMonth = DateTime.DaysInMonth(selectedYear, selectedMonth);
            if (selectedDay > daysInMonth)
            {
                selectedDay = daysInMonth;
            }

            var startDate = new DateTime(selectedYear, selectedMonth, 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);

            // حساب الحجوزات المسجلة خلال الشهر المحدد (بناءً على تاريخ الإنشاء CreatedAt)
            var monthlyBookings = await _context.Bookings
                .Where(b => b.CreatedAt >= startDate && b.CreatedAt <= endDate && b.Status != Models.BookingStatus.Canceled)
                .ToListAsync();

            ViewBag.TotalRevenue = monthlyBookings.Sum(b => b.TotalAmount);
            ViewBag.TotalPaid = monthlyBookings.Sum(b => b.PaidAmount);
            ViewBag.TotalRemaining = monthlyBookings.Sum(b => b.RemainingAmount);
            ViewBag.TotalBookingsCount = monthlyBookings.Count;

            ViewBag.SelectedYear = selectedYear;
            ViewBag.SelectedMonth = selectedMonth;
            ViewBag.SelectedDay = selectedDay;

            // حساب إيرادات اليوم المختار بناءً على تاريخ إجراء عملية الحجز (CreatedAt)
            DateTime selectedDate = new DateTime(selectedYear, selectedMonth, selectedDay);
            ViewBag.DailyRevenue = await _context.Bookings
                .Where(b => b.CreatedAt.Date == selectedDate.Date && b.Status != Models.BookingStatus.Canceled)
                .SumAsync(b => b.PaidAmount);

            return View();
        }

        // 2. عرض قائمة المستخدمين
        [HttpGet]
        public async Task<IActionResult> Users()
        {
            var users = await _userManager.Users.ToListAsync();
            var userRolesViewModel = new List<UserViewModel>();

            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);
                userRolesViewModel.Add(new UserViewModel
                {
                    Id = user.Id,
                    FullName = user.FullName ?? user.UserName,
                    Email = user.Email,
                    Role = roles.FirstOrDefault() ?? "بدون دور"
                });
            }

            return View(userRolesViewModel);
        }

        // 3. إنشاء مستخدم جديد بواسطة الأدمن
        [HttpPost]
        public async Task<IActionResult> CreateUser(string fullName, string email, string password, string role)
        {
            if (ModelState.IsValid)
            {
                var existingUser = await _userManager.FindByEmailAsync(email);
                if (existingUser != null)
                {
                    TempData["Error"] = "البريد الإلكتروني مستخدم بالفعل لنظام آخر.";
                    return RedirectToAction(nameof(Users));
                }

                var newUser = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    FullName = fullName,
                    EmailConfirmed = true
                };

                var result = await _userManager.CreateAsync(newUser, password);
                if (result.Succeeded)
                {
                    if (await _roleManager.RoleExistsAsync(role))
                    {
                        await _userManager.AddToRoleAsync(newUser, role);
                    }

                    // تسجيل العملية في Audit Log
                    var currentUser = await _userManager.GetUserAsync(User);
                    await _auditService.LogAsync(
                        currentUser.Id,
                        currentUser.FullName ?? currentUser.UserName,
                        "إنشاء مستخدم",
                        "ApplicationUser",
                        0,
                        $"قام الأدمن بإنشاء حساب جديد للموظف [{fullName}] بصلاحية [{role}]"
                    );

                    TempData["Success"] = $"تم إنشاء حساب ({fullName}) بنجاح.";
                    return RedirectToAction(nameof(Users));
                }

                foreach (var error in result.Errors)
                {
                    TempData["Error"] = error.Description;
                }
            }

            return RedirectToAction(nameof(Users));
        }

        // 4. حذف حساب مستخدم
        [HttpPost]
        public async Task<IActionResult> DeleteUser(string id)
        {
            var currentUser = await _userManager.GetUserAsync(User);

            // منع الأدمن من حذف حسابه الشخصي المسجل به حالياً
            if (currentUser != null && currentUser.Id == id)
            {
                TempData["Error"] = "لا يمكنك حذف الحساب الخاص بك أثناء تسجيل الدخول منه!";
                return RedirectToAction(nameof(Users));
            }

            var userToDelete = await _userManager.FindByIdAsync(id);
            if (userToDelete == null)
            {
                TempData["Error"] = "المستخدم غير موجود.";
                return RedirectToAction(nameof(Users));
            }

            string userNameToDelete = userToDelete.FullName ?? userToDelete.UserName;
            var result = await _userManager.DeleteAsync(userToDelete);

            if (result.Succeeded)
            {
                await _auditService.LogAsync(
                    currentUser.Id,
                    currentUser.FullName ?? currentUser.UserName,
                    "حذف مستخدم",
                    "ApplicationUser",
                    0,
                    $"قام الأدمن بحذف حساب الموظف [{userNameToDelete}]"
                );

                TempData["Success"] = $"تم حذف حساب الموظف ({userNameToDelete}) بنجاح.";
            }
            else
            {
                TempData["Error"] = "حدث خطأ أثناء حذف حساب المستخدم.";
            }

            return RedirectToAction(nameof(Users));
        }

        // 5. سجل الحركات
        public async Task<IActionResult> AuditLogs()
        {
            var logs = await _context.AuditLogs
                .OrderByDescending(l => l.Timestamp)
                .ToListAsync();

            return View(logs);
        }
    }

    public class UserViewModel
    {
        public string Id { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }
        public string Role { get; set; }
    }
}