using NileJewal.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace NileJewal.Controllers
{
    public class AccountController : Controller
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager; // تم إضافة الـ UserManager هنا

        public AccountController(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager)
        {
            _signInManager = signInManager;
            _userManager = userManager;
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(string email, string password, string? returnUrl = null)
        {
            returnUrl ??= Url.Action("DailyGrid", "Booking");

            if (ModelState.IsValid)
            {
                var result = await _signInManager.PasswordSignInAsync(email, password, false, lockoutOnFailure: false);
                if (result.Succeeded)
                {
                    // جلب المستخدم وفحص دوره لتوجيه خدمة العملاء مباشرة
                    var user = await _userManager.FindByEmailAsync(email);
                    if (user != null)
                    {
                        var roles = await _userManager.GetRolesAsync(user);
                        if (roles.Contains("CService"))
                        {
                            return RedirectToAction("AdminBookingsReport", "Booking");
                        }
                    }

                    return LocalRedirect(returnUrl);
                }
                ModelState.AddModelError(string.Empty, "البريد الإلكتروني أو كلمة المرور غير صحيحة");
            }

            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Login", "Account");
        }
    }
}