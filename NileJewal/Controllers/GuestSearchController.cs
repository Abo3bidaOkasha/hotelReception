using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NileJewal.Data; // استبدلها بـ Namespace الخاص بالـ DbContext
using NileJewal.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NileJewal.Controllers
{
    public class GuestSearchController : Controller
    {
        private readonly ApplicationDbContext _context;

        public GuestSearchController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(string searchTerm)
        {
            ViewData["SearchTerm"] = searchTerm;

            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return View(new List<Booking>());
            }

            string term = searchTerm.Trim();

            // البحث باسم النزيل أو برقم الهاتف
            var bookings = await _context.Bookings
                .Where(b => EF.Functions.Like(b.GuestName, $"%{term}%")
                         || EF.Functions.Like(b.GuestPhone, $"%{term}%")) // افترضنا أن اسم الحقل PhoneNumber
                .OrderByDescending(b => b.CheckInDate)
                .ToListAsync();

            return View(bookings);
        }
    }
}