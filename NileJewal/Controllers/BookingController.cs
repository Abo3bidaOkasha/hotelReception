using NileJewal.Data;
using NileJewal.Models;
using NileJewal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace NileJewal.Controllers
{
    [Authorize]
    public class BookingController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IAuditService _auditService;
       
        public BookingController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IAuditService auditService)
        {
            _context = context;
            _userManager = userManager;
             _auditService = auditService;
        }
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> SearchAvailable(DateTime? startDate, DateTime? endDate)
        {
            // لو لسه المستخدم ما دخلش تاريخ، نخليها افتراضياً من النهاردة لبكرة
            var start = startDate ?? DateTime.Today;
            var end = endDate ?? DateTime.Today.AddDays(1);

            ViewBag.StartDate = start.ToString("yyyy-MM-dd");
            ViewBag.EndDate = end.ToString("yyyy-MM-dd");

            // جلب الغرف التي ليس لها حجوزات متداخلة مع الفترة المحددة
            var availableRooms = await _context.Rooms
                .Include(r => r.Bookings)
                .Where(r => !r.Bookings.Any(b =>
                    b.CheckInDate < end && b.CheckOutDate > start
                ))
                .ToListAsync();

            return View(availableRooms);
        }

        [HttpGet]
        [Authorize(Roles = "Admin,Receptionist")]
        public async Task<IActionResult> Create(int roomId, DateTime? startDate, DateTime? endDate)
        {
            var room = await _context.Rooms.FindAsync(roomId);
            if (room == null)
            {
                return NotFound();
            }

            var booking = new Booking
            {
                RoomId = roomId,
                CheckInDate = startDate ?? DateTime.Today,
                CheckOutDate = endDate ?? DateTime.Today.AddDays(1)
                // قم بإزالة سطر PricePerNight أو كتابة الخاصية الصحيحة الموجودة في كلاس Room لديك
            };

            ViewBag.RoomNumber = room.RoomNumber;
            return View(booking);
        }

        [Authorize(Roles = "Admin,Receptionist")]
        public async Task<IActionResult> DailyGrid(DateTime? date)
        {
            var selectedDate = date ?? DateTime.Today;
            ViewBag.SelectedDate = selectedDate;

            // جلب الغرف والحجوزات النشطة في اليوم المختار
            var rooms = await _context.Rooms
                .Include(r => r.Bookings
                    .Where(b => b.CheckInDate.Date <= selectedDate.Date
                            && b.CheckOutDate.Date > selectedDate.Date
                            && b.Status != BookingStatus.Canceled)
                    .OrderByDescending(b => b.RoomType == "MU"))
                .OrderBy(r => r.RoomNumber)
                .ToListAsync();

            return View(rooms);
        }

        [HttpGet]
        [Authorize(Roles = "Admin,Receptionist")]
        public async Task<IActionResult> Invoice(int id)
        {
            var booking = await _context.Bookings
                .Include(b => b.Room)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null)
            {
                return NotFound();
            }

            return View(booking);
        }

        [HttpPost]
        [Authorize(Roles = "Admin,Receptionist")]
        public async Task<IActionResult> Create(Booking booking)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "برجاء التأكد من إدخال جميع البيانات بشكل صحيح.";
                return RedirectToAction(nameof(DailyGrid), new { date = booking.CheckInDate.ToString("yyyy-MM-dd") });
            }

            var currentUser = await _userManager.GetUserAsync(User);

            // 1. فحص التعارضات السابقة للغرفة
            var existingBookings = await _context.Bookings
                .Where(b => b.RoomId == booking.RoomId
                       && b.Status != BookingStatus.Canceled
                       && booking.CheckInDate < b.CheckOutDate
                       && booking.CheckOutDate > b.CheckInDate)
                .ToListAsync();

            if (existingBookings.Any())
            {
                // السماح فقط بالحجز المزدوج إذا كان الحجز الموجود من نوع MU والحجز الجديد من نوع إسكان
                bool isMuOverHousingAllowed = existingBookings.Count == 1
                                           && existingBookings.First().RoomType == "MU"
                                           && booking.RoomType != "MU";

                if (!isMuOverHousingAllowed)
                {
                    TempData["Error"] = "تعذر الحجز! الغرفة محجوزة بالفعل في هذا التاريخ بحجز لا يسمح بالإسكان المزدوج.";
                    return RedirectToAction(nameof(DailyGrid), new { date = booking.CheckInDate.ToString("yyyy-MM-dd") });
                }
            }

            // 2. احتساب عدد الليالي والأغذية والإجمالي
            var nights = (int)(booking.CheckOutDate - booking.CheckInDate).TotalDays;
            if (nights <= 0) nights = 1;

            if (booking.HasFood)
            {
                booking.TotalFoodAmount = booking.DailyFoodAmount * nights;
                if (string.IsNullOrEmpty(booking.MealType))
                {
                    booking.MealType = "إفطار";
                }
            }
            else
            {
                booking.MealType = "بدون وجبات";
                booking.DailyFoodAmount = 0;
                booking.TotalFoodAmount = 0;
            }

            booking.TotalAmount = (nights * booking.PricePerNight) + booking.TotalFoodAmount;
            booking.CreatedByUserId = currentUser.Id;
            booking.CreatedByUserName = currentUser.FullName ?? currentUser.UserName;
            booking.CreatedAt = DateTime.Now;

            _context.Bookings.Add(booking);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                currentUser.Id,
                currentUser.FullName ?? currentUser.UserName,
                "إضافة حجز",
                "Booking",
                booking.Id,
                $"إضافة حجز للنزيل [{booking.GuestName}] - غرفة رقم [{booking.RoomId}] - نوع [{booking.RoomType}] - إجمالي الأغذية [{booking.TotalFoodAmount}] - إجمالي المبلغ [{booking.TotalAmount}]"
            );

            TempData["Success"] = "تم إضافة الحجز بنجاح.";
            return RedirectToAction(nameof(DailyGrid), new { date = booking.CheckInDate.ToString("yyyy-MM-dd") });
        }

        [HttpPost]
        [Authorize(Roles = "Admin,Receptionist")]
        public async Task<IActionResult> Edit(Booking updatedBooking)
        {
            var oldBooking = await _context.Bookings.FirstOrDefaultAsync(b => b.Id == updatedBooking.Id);
            if (oldBooking == null) return NotFound();

            if (!ModelState.IsValid)
            {
                TempData["Error"] = "برجاء التأكد من صحة البيانات المدخلة للتعديل.";
                return RedirectToAction(nameof(DailyGrid), new { date = oldBooking.CheckInDate.ToString("yyyy-MM-dd") });
            }

            // فحص التعارض عند التعديل (مع استثناء الحجز الحالي من الفحص)
            var conflictingBookings = await _context.Bookings
                .Where(b => b.RoomId == oldBooking.RoomId
                       && b.Id != updatedBooking.Id
                       && b.Status != BookingStatus.Canceled
                       && updatedBooking.CheckInDate < b.CheckOutDate
                       && updatedBooking.CheckOutDate > b.CheckInDate)
                .ToListAsync();

            if (conflictingBookings.Any())
            {
                bool isMuOverHousingAllowed = conflictingBookings.Count == 1
                                           && conflictingBookings.First().RoomType == "MU"
                                           && updatedBooking.RoomType != "MU";

                if (!isMuOverHousingAllowed)
                {
                    TempData["Error"] = "تعذر تعديل الحجز! التواريخ الجديدة تتعارض مع حجز آخر قائم للغرفة.";
                    return RedirectToAction(nameof(DailyGrid), new { date = oldBooking.CheckInDate.ToString("yyyy-MM-dd") });
                }
            }

            var nights = (int)(updatedBooking.CheckOutDate - updatedBooking.CheckInDate).TotalDays;
            if (nights <= 0) nights = 1;

            if (updatedBooking.HasFood)
            {
                updatedBooking.TotalFoodAmount = updatedBooking.DailyFoodAmount * nights;
                if (string.IsNullOrEmpty(updatedBooking.MealType))
                {
                    updatedBooking.MealType = "إفطار";
                }
            }
            else
            {
                updatedBooking.MealType = "بدون وجبات";
                updatedBooking.DailyFoodAmount = 0;
                updatedBooking.TotalFoodAmount = 0;
            }

            var newTotalAmount = (nights * updatedBooking.PricePerNight) + updatedBooking.TotalFoodAmount;

            string details = $"تعديل حجز #{updatedBooking.Id}: " +
                             $"[الاسم: {oldBooking.GuestName} ⬅️ {updatedBooking.GuestName}]، " +
                             $"[نوع الغرفة: {oldBooking.RoomType} ⬅️ {updatedBooking.RoomType}]، " +
                             $"[نوع الوجبة: {oldBooking.MealType} ⬅️ {updatedBooking.MealType}]، " +
                             $"[الأغذية: {oldBooking.TotalFoodAmount} ⬅️ {updatedBooking.TotalFoodAmount}]، " +
                             $"[المبلغ الإجمالي: {oldBooking.TotalAmount} ⬅️ {newTotalAmount}]، " +
                             $"[المدفوع: {oldBooking.PaidAmount} ⬅️ {updatedBooking.PaidAmount}]";

            oldBooking.GuestName = updatedBooking.GuestName;
            oldBooking.GuestPhone = updatedBooking.GuestPhone;
            oldBooking.RoomType = updatedBooking.RoomType;
            oldBooking.OccupancyType = updatedBooking.OccupancyType;
            oldBooking.CheckInDate = updatedBooking.CheckInDate;
            oldBooking.CheckOutDate = updatedBooking.CheckOutDate;
            oldBooking.PricePerNight = updatedBooking.PricePerNight;

            oldBooking.MealType = updatedBooking.MealType;
            oldBooking.HasFood = updatedBooking.HasFood;
            oldBooking.DailyFoodAmount = updatedBooking.DailyFoodAmount;
            oldBooking.TotalFoodAmount = updatedBooking.TotalFoodAmount;
            oldBooking.TotalAmount = newTotalAmount;

            oldBooking.PaidAmount = updatedBooking.PaidAmount;
            oldBooking.ReceiptNumber = updatedBooking.ReceiptNumber;
            oldBooking.Notes = updatedBooking.Notes;

            await _context.SaveChangesAsync();

            var currentUser = await _userManager.GetUserAsync(User);
            await _auditService.LogAsync(
                currentUser.Id,
                currentUser.FullName ?? currentUser.UserName,
                "تعديل حجز",
                "Booking",
                updatedBooking.Id,
                details
            );

            TempData["Success"] = "تم تعديل بيانات الحجز بنجاح.";
            return RedirectToAction(nameof(DailyGrid), new { date = oldBooking.CheckInDate.ToString("yyyy-MM-dd") });
        }

        [HttpPost]
        [Authorize(Roles = "Admin,Receptionist")]
        public async Task<IActionResult> Delete(int id)
        {
            var booking = await _context.Bookings.FindAsync(id);
            if (booking == null) return NotFound();

            var currentUser = await _userManager.GetUserAsync(User);
            var bookingDate = booking.CheckInDate;

            _context.Bookings.Remove(booking);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                currentUser.Id,
                currentUser.FullName ?? currentUser.UserName,
                "حذف حجز",
                "Booking",
                id,
                $"حذف حجز النزيل [{booking.GuestName}] - غرفة رقم [{booking.RoomId}] - مبلغ متبقي [{booking.RemainingAmount}]"
            );

            TempData["Success"] = "تم حذف الحجز بنجاح.";
            return RedirectToAction(nameof(DailyGrid), new { date = bookingDate.ToString("yyyy-MM-dd") });
        }
        [HttpPost]
        [Authorize(Roles = "Admin,Receptionist")]
        public async Task<IActionResult> ToggleCheckOutStatus(int id)
        {
            var booking = await _context.Bookings.FindAsync(id);
            if (booking != null)
            {
                // عكس الحالة الحالية (إذا كانت false تصبح true والعكس)
                booking.IsCheckedOut = !booking.IsCheckedOut;
                await _context.SaveChangesAsync();
            }
            return RedirectToAction("CheckOutToday");
        }
        [Authorize(Roles = "Admin,Receptionist")]
        public async Task<IActionResult> CheckOutToday()
        {
            var today = DateTime.Today;

            // جلب الحجوزات التي تاريخ مغادرتها اليوم وليست ملغاة
            var bookings = await _context.Bookings
                .Include(b => b.Room)
                .Where(b => b.CheckOutDate.Date == today && b.Status != BookingStatus.Canceled)
                .OrderBy(b => b.Room.RoomNumber)
                .ToListAsync();

            return View(bookings);
        }

        [Authorize(Roles = "Admin,Receptionist")]
        public async Task<IActionResult> ArrivingToday()
        {
            var today = DateTime.Today;

            // جلب الحجوزات التي تاريخ وصولها اليوم وليست ملغاة
            var bookings = await _context.Bookings
                .Include(b => b.Room)
                .Where(b => b.CheckInDate.Date == today && b.Status != BookingStatus.Canceled)
                .OrderBy(b => b.Room.RoomNumber)
                .ToListAsync();

            return View(bookings);
        }
        [HttpPost]
        [Authorize(Roles = "Admin,Receptionist")]
        public async Task<IActionResult> ToggleCheckInStatus(int id)
        {
            var booking = await _context.Bookings.FindAsync(id);
            if (booking != null)
            {
                booking.IsCheckedIn = !booking.IsCheckedIn;
                await _context.SaveChangesAsync();
            }
            return RedirectToAction("ArrivingToday");
        }

        [HttpPost]
        [Authorize(Roles = "Admin,Receptionist")]
        public async Task<IActionResult> Transfer(int bookingId, string newRoomNumber)
        {
            // التحقق من أن رقم الغرفة غير فارغ لتجنب الخطأ الظاهر في الواجهة
            if (string.IsNullOrWhiteSpace(newRoomNumber))
            {
                TempData["Error"] = "برجاء إدخال رقم الغرفة المراد النقل إليها بشكل صحيح.";
                return RedirectToAction(nameof(DailyGrid));
            }

            var bookingToTransfer = await _context.Bookings
                .Include(b => b.Room)
                .FirstOrDefaultAsync(b => b.Id == bookingId);

            if (bookingToTransfer == null)
            {
                TempData["Error"] = "الحجز المراد نقله غير موجود.";
                return RedirectToAction(nameof(DailyGrid));
            }

            var targetRoom = await _context.Rooms
                .FirstOrDefaultAsync(r => r.RoomNumber == newRoomNumber && r.IsActive);

            if (targetRoom == null)
            {
                TempData["Error"] = $"الغرفة رقم ({newRoomNumber}) غير موجودة بالنظام أو غير مفعلة.";
                return RedirectToAction(nameof(DailyGrid), new { date = bookingToTransfer.CheckInDate.ToString("yyyy-MM-dd") });
            }

            if (targetRoom.Id == bookingToTransfer.RoomId)
            {
                TempData["Error"] = $"النزيل موجود بالفعل في الغرفة رقم ({newRoomNumber}).";
                return RedirectToAction(nameof(DailyGrid), new { date = bookingToTransfer.CheckInDate.ToString("yyyy-MM-dd") });
            }

            // فحص التعارض للغرفة المنقول إليها
            var conflictingBookings = await _context.Bookings
                .Where(b => b.RoomId == targetRoom.Id
                       && b.Id != bookingId
                       && b.Status != BookingStatus.Canceled
                       && bookingToTransfer.CheckInDate < b.CheckOutDate
                       && bookingToTransfer.CheckOutDate > b.CheckInDate)
                .ToListAsync();

            if (conflictingBookings.Any())
            {
                bool canTransferUnderMU = conflictingBookings.Count == 1
                                        && conflictingBookings.First().RoomType == "MU"
                                        && bookingToTransfer.RoomType != "MU";

                if (!canTransferUnderMU)
                {
                    var conflict = conflictingBookings.First();
                    TempData["Error"] = $"تعذر النقل! الغرفة ({newRoomNumber}) مرتبطة بحجز آخر للنزيل [{conflict.GuestName}] في الفترة من ({conflict.CheckInDate:yyyy/MM/dd}) إلى ({conflict.CheckOutDate:yyyy/MM/dd}).";
                    return RedirectToAction(nameof(DailyGrid), new { date = bookingToTransfer.CheckInDate.ToString("yyyy-MM-dd") });
                }
            }

            string oldRoomNumber = bookingToTransfer.Room?.RoomNumber ?? bookingToTransfer.RoomId.ToString();
            bookingToTransfer.RoomId = targetRoom.Id;

            _context.Bookings.Update(bookingToTransfer);
            await _context.SaveChangesAsync();

            var currentUser = await _userManager.GetUserAsync(User);
            await _auditService.LogAsync(
                currentUser.Id,
                currentUser.FullName ?? currentUser.UserName,
                "نقل غرفة",
                "Booking",
                bookingToTransfer.Id,
                $"نقل النزيل [{bookingToTransfer.GuestName}] من الغرفة [{oldRoomNumber}] إلى الغرفة [{newRoomNumber}] للفترة من [{bookingToTransfer.CheckInDate:yyyy/MM/dd}] إلى [{bookingToTransfer.CheckOutDate:yyyy/MM/dd}]"
            );

            TempData["Success"] = $"تم نقل النزيل ({bookingToTransfer.GuestName}) بنجاح إلى الغرفة ({newRoomNumber}).";
            return RedirectToAction(nameof(DailyGrid), new { date = bookingToTransfer.CheckInDate.ToString("yyyy-MM-dd") });
        }
    }
}