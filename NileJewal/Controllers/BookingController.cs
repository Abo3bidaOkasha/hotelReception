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

            if (updatedBooking.RoomId == 0)
            {
                updatedBooking.RoomId = oldBooking.RoomId;
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

            oldBooking.RoomId = updatedBooking.RoomId;
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

        [Authorize(Roles = "Admin,Receptionist")]
        [HttpGet]
        public async Task<IActionResult> PrintDailyReport(DateTime? date)
        {
            var selectedDate = date ?? DateTime.Today;
            ViewBag.SelectedDate = selectedDate;

            // جلب الغرف مع الحجز النشط في هذا التاريخ بالذات
            var rooms = await _context.Rooms
                .Include(r => r.Bookings.Where(b => b.CheckInDate.Date <= selectedDate.Date && b.CheckOutDate.Date > selectedDate.Date && !b.IsCheckedOut))
                .OrderBy(r => r.RoomNumber)
                .ToListAsync();

            return View(rooms);
        }

        [HttpGet]
        public async Task<IActionResult> ExportDailyReportExcel(DateTime? date)
        {
            var selectedDate = date ?? DateTime.Today;

            var rooms = await _context.Rooms
                .Include(r => r.Bookings.Where(b => b.CheckInDate.Date <= selectedDate.Date && b.CheckOutDate.Date > selectedDate.Date && !b.IsCheckedOut))
                .OrderBy(r => r.RoomNumber)
                .ToListAsync();

            var excelContent = new System.Text.StringBuilder();

            excelContent.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            excelContent.AppendLine("<?mso-application progid=\"Excel.Sheet\"?>");
            excelContent.AppendLine("<Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\"");
            excelContent.AppendLine("          xmlns:o=\"urn:schemas-microsoft-com:office:office\"");
            excelContent.AppendLine("          xmlns:x=\"urn:schemas-microsoft-com:office:excel\"");
            excelContent.AppendLine("          xmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\"");
            excelContent.AppendLine("          xmlns:html=\"http://www.w3.org/TR/REC-html40\">");

            // تعريف التنسيقات (الخطوط، الحدود، الخلفيات)
            excelContent.AppendLine(" <Styles>");
            excelContent.AppendLine("  <Style ss:ID=\"Default\" ss:Name=\"Normal\">");
            excelContent.AppendLine("   <Alignment ss:Vertical=\"Center\" ss:Horizontal=\"Center\"/>");
            excelContent.AppendLine("   <Font ss:FontName=\"Cairo\" ss:Size=\"11\"/>");
            excelContent.AppendLine("   <Borders/>");
            excelContent.AppendLine("  </Style>");

            // تنسيق العنوان الرئيسي
            excelContent.AppendLine("  <Style ss:ID=\"TitleStyle\">");
            excelContent.AppendLine("   <Alignment ss:Horizontal=\"Center\" ss:Vertical=\"Center\"/>");
            excelContent.AppendLine("   <Font ss:FontName=\"Cairo\" ss:Size=\"14\" ss:Bold=\"1\"/>");
            excelContent.AppendLine("  </Style>");

            // تنسيق رأس الجدول (Background رمادي فاتح + خطوط + غامق)
            excelContent.AppendLine("  <Style ss:ID=\"HeaderStyle\">");
            excelContent.AppendLine("   <Alignment ss:Horizontal=\"Center\" ss:Vertical=\"Center\"/>");
            excelContent.AppendLine("   <Font ss:FontName=\"Cairo\" ss:Size=\"11\" ss:Bold=\"1\"/>");
            excelContent.AppendLine("   <Interior ss:Color=\"#E0E0E0\" ss:Pattern=\"Solid\"/>");
            excelContent.AppendLine("   <Borders>");
            excelContent.AppendLine("    <Border ss:Position=\"Bottom\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\"/>");
            excelContent.AppendLine("    <Border ss:Position=\"Left\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\"/>");
            excelContent.AppendLine("    <Border ss:Position=\"Right\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\"/>");
            excelContent.AppendLine("    <Border ss:Position=\"Top\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\"/>");
            excelContent.AppendLine("   </Borders>");
            excelContent.AppendLine("  </Style>");

            // تنسيق خلايا الجدول العادية (بحدود واضحة وموسّطة)
            excelContent.AppendLine("  <Style ss:ID=\"CellStyle\">");
            excelContent.AppendLine("   <Alignment ss:Horizontal=\"Center\" ss:Vertical=\"Center\"/>");
            excelContent.AppendLine("   <Font ss:FontName=\"Cairo\" ss:Size=\"11\"/>");
            excelContent.AppendLine("   <Borders>");
            excelContent.AppendLine("    <Border ss:Position=\"Bottom\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D3D3D3\"/>");
            excelContent.AppendLine("    <Border ss:Position=\"Left\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D3D3D3\"/>");
            excelContent.AppendLine("    <Border ss:Position=\"Right\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D3D3D3\"/>");
            excelContent.AppendLine("    <Border ss:Position=\"Top\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D3D3D3\"/>");
            excelContent.AppendLine("   </Borders>");
            excelContent.AppendLine("  </Style>");
            excelContent.AppendLine(" </Styles>");

            excelContent.AppendLine(" <Worksheet ss:Name=\"تقرير الحجوزات اليومي\">");
            excelContent.AppendLine("  <Table>");
            excelContent.AppendLine("   <WorksheetOptions xmlns=\"urn:schemas-microsoft-com:office:excel\">");
            excelContent.AppendLine("    <DisplayRightToLeft/>");
            excelContent.AppendLine("   </WorksheetOptions>");

            // العنوان
            excelContent.AppendLine($"   <Row><Cell ss:StyleID=\"TitleStyle\" ss:MergeAcross=\"5\"><Data ss:Type=\"String\">تقرير الحجوزات اليومي - بتاريخ: {selectedDate:yyyy/MM/dd}</Data></Cell></Row>");
            excelContent.AppendLine("   <Row></Row>");

            // رؤوس الأعمدة بالتنسيق المطابق للورقة تماماً (من اليمين لليسار)
            excelContent.AppendLine("   <Row>");
            excelContent.AppendLine("    <Cell ss:StyleID=\"HeaderStyle\"><Data ss:Type=\"String\">رقم الغرفة</Data></Cell>");
            excelContent.AppendLine("    <Cell ss:StyleID=\"HeaderStyle\"><Data ss:Type=\"String\">دخول IN</Data></Cell>");
            excelContent.AppendLine("    <Cell ss:StyleID=\"HeaderStyle\"><Data ss:Type=\"String\">خروج OUT</Data></Cell>");
            excelContent.AppendLine("    <Cell ss:StyleID=\"HeaderStyle\"><Data ss:Type=\"String\">اسم النزيل</Data></Cell>");
            excelContent.AppendLine("    <Cell ss:StyleID=\"HeaderStyle\"><Data ss:Type=\"String\">المرافقين</Data></Cell>");
            excelContent.AppendLine("    <Cell ss:StyleID=\"HeaderStyle\"><Data ss:Type=\"String\">نوع الغرفة</Data></Cell>");
            excelContent.AppendLine("   </Row>");

            foreach (var room in rooms)
            {
                var activeBooking = room.Bookings?.FirstOrDefault();
                string roomNum = room.RoomNumber ?? "";
                string checkIn = activeBooking != null ? activeBooking.CheckInDate.ToString("yyyy/MM/dd") : "";
                string checkOut = activeBooking != null ? activeBooking.CheckOutDate.ToString("yyyy/MM/dd") : "";
                string guestName = activeBooking != null ? activeBooking.GuestName : "";
                string companions = "";

                // تحديد نوع الغرفة: من 401 إلى 408 نايل فيو والباقي خلفي
                string roomViewType = "خلفي";
                if (int.TryParse(roomNum, out int roomNo))
                {
                    if (roomNo >= 401 && roomNo <= 408 )
                    {
                        roomViewType = "نايل فيو";
                    }
                    if (roomNo == 432)
                    {
                        roomViewType = "نايل فيو";
                    }
                }

                excelContent.AppendLine("   <Row>");
                excelContent.AppendLine($"    <Cell ss:StyleID=\"CellStyle\"><Data ss:Type=\"String\">{roomNum}</Data></Cell>");
                excelContent.AppendLine($"    <Cell ss:StyleID=\"CellStyle\"><Data ss:Type=\"String\">{checkIn}</Data></Cell>");
                excelContent.AppendLine($"    <Cell ss:StyleID=\"CellStyle\"><Data ss:Type=\"String\">{checkOut}</Data></Cell>");
                excelContent.AppendLine($"    <Cell ss:StyleID=\"CellStyle\"><Data ss:Type=\"String\">{System.Net.WebUtility.HtmlEncode(guestName)}</Data></Cell>");
                excelContent.AppendLine($"    <Cell ss:StyleID=\"CellStyle\"><Data ss:Type=\"String\">{companions}</Data></Cell>");
                excelContent.AppendLine($"    <Cell ss:StyleID=\"CellStyle\"><Data ss:Type=\"String\">{roomViewType}</Data></Cell>");
                excelContent.AppendLine("   </Row>");
            }

            excelContent.AppendLine("  </Table>");
            excelContent.AppendLine(" </Worksheet>");
            excelContent.AppendLine("</Workbook>");

            var bytes = System.Text.Encoding.UTF8.GetBytes(excelContent.ToString());
            return File(bytes, "application/vnd.ms-excel", $"Daily_Report_{selectedDate:yyyy-MM-dd}.xls");
        }

        [HttpGet]
        public async Task<IActionResult> ExportBreakfastReportExcel(DateTime? date)
        {
            var selectedDate = date ?? DateTime.Today;
            var rooms = await _context.Rooms
                .Include(r => r.Bookings.Where(b => b.CheckInDate.Date <= selectedDate.Date && b.CheckOutDate.Date > selectedDate.Date && !b.IsCheckedOut))
                .OrderBy(r => r.RoomNumber)
                .ToListAsync();

            var sb = new System.Text.StringBuilder();

            sb.AppendLine("<html xmlns:o='urn:schemas-microsoft-com:office:office' xmlns:x='urn:schemas-microsoft-com:office:excel' xmlns='http://www.w3.org/TR/REC-html40'>");
            sb.AppendLine("<head><meta charset='utf-8'>");
            sb.AppendLine("<style>");
            sb.AppendLine("body { direction: rtl; }");
            sb.AppendLine("table { border-collapse: collapse; direction: rtl; font-family: 'Cairo', Tahoma, sans-serif; }");
            sb.AppendLine("th { background-color: #e0e0e0; color: #000; padding: 6px; border: 1px solid #000; font-weight: bold; text-align: center; font-size: 11px; }");
            sb.AppendLine("td { padding: 5px; border: 1px solid #000; text-align: center; font-size: 11px; vertical-align: middle; height: 8px; }");
            sb.AppendLine(".title-cell { font-size: 13px; font-weight: bold; text-align: center; background-color: #f8f9fa; border: 1px solid #000; padding:1px; }");
            sb.AppendLine(".total-row td { font-weight: bold; background-color: #f1f1f1; border: 1px solid #000; }");
            sb.AppendLine("</style>");

            // إخبار برنامج الإكسيل بفتح الملف باتجاه اليمين لليسار رسمياً
            sb.AppendLine("<xml><x:ExcelWorkbook><x:ExcelWorksheets><x:ExcelWorksheet>");
            sb.AppendLine("<x:Name>بلان الفطار</x:Name>");
            sb.AppendLine("<x:WorksheetOptions><x:DisplayRightToLeft/></x:WorksheetOptions>");
            sb.AppendLine("</x:ExcelWorksheet></x:ExcelWorkbook></xml>");

            sb.AppendLine("</head><body>");

            sb.AppendLine("<table>");

            // سطر العنوان مدمج على الثلاثة أعمدة من اليمين
            sb.AppendLine($"<tr><td colspan='3' class='title-cell'>بلان فطار نايل جويل يوم {selectedDate:yyyy/MM/dd}</td></tr>");
            sb.AppendLine("<tr><td colspan='3' style='border:none; height:10px;'></td></tr>");

            // رؤوس الأعمدة بالترتيب الصحيح المطابق لصورتك (الغرف، عدد الفطار، ملاحظات)
            sb.AppendLine("<tr>");
            sb.AppendLine("<th style='width: 70px;'>الغرف</th>");
            sb.AppendLine("<th style='width: 85px;'>عدد الفطار</th>");
            sb.AppendLine("<th style='width: 250px;'>ملاحظات</th>");
            sb.AppendLine("</tr>");

            int totalBreakfast = 0;

            foreach (var room in rooms)
            {
                var activeBooking = room.Bookings?.FirstOrDefault();
                string roomNum = room.RoomNumber ?? "";
                string breakfastCountStr = "";
                string notes = "";

                if (activeBooking != null)
                {
                    string occType = (activeBooking.OccupancyType ?? activeBooking.RoomType ?? "").ToUpper();
                    string notesField = (activeBooking.Notes ?? "").ToUpper();

                    // استبعاد غرف الـ MU تماماً
                    bool isMu = occType.Contains("MU") || notesField.Contains("MU");

                    if (!isMu)
                    {
                        if (occType.Contains("SINGLE") || occType.Contains("سنجل")) { breakfastCountStr = "1"; totalBreakfast += 1; }
                        else if (occType.Contains("DOUBLE") || occType.Contains("دبل")) { breakfastCountStr = "2"; totalBreakfast += 2; }
                        else if (occType.Contains("TRIPLE") || occType.Contains("تريبل")) { breakfastCountStr = "3"; totalBreakfast += 3; }
                        else { breakfastCountStr = "1"; totalBreakfast += 1; }
                    }
                }

                sb.AppendLine("<tr>");
                sb.AppendLine($"<td>{roomNum}</td>");
                sb.AppendLine($"<td>{breakfastCountStr}</td>");
                sb.AppendLine($"<td>{notes}</td>");
                sb.AppendLine("</tr>");
            }

            // صف الإجمالي في النهاية
            sb.AppendLine("<tr class='total-row'>");
            sb.AppendLine("<td>الإجمالي</td>");
            sb.AppendLine($"<td>{totalBreakfast}</td>");
            sb.AppendLine("<td></td>");
            sb.AppendLine("</tr>");

            sb.AppendLine("</table></body></html>");

            var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
            // استخدام امتداد .xls لمنع ظهور أي تحذيرات ولفتح الملف كإكسيل معتمد ومثالي
            return File(bytes, "application/vnd.ms-excel", $"Breakfast_Plan_{selectedDate:yyyy-MM-dd}.xls");
        }

        [Authorize(Roles = "Admin,CService")]
        public async Task<IActionResult> AdminBookingsReport(DateTime? date)
        {
            // إذا لم يتم تحديد تاريخ، اجعله تاريخ اليوم
            DateTime selectedDate = date ?? DateTime.Today;

            // جلب الحجوزات التي يكون تاريخ الدخول فيها مطابقاً تماماً للتاريخ المختار
            var bookings = await _context.Bookings
                .Where(b => b.CheckInDate.Date == selectedDate.Date)
                .OrderByDescending(b => b.CheckInDate)
                .ToListAsync();

            // تمرير التاريخ المحدد للـ View ليبقى مخزناً في حقل الإدخال
            ViewBag.SelectedDate = selectedDate.ToString("yyyy-MM-dd");

            return View(bookings);
        }
        [HttpPost]
        [Authorize(Roles = "Admin,CService")]
        public async Task<IActionResult> UpdateContactInfo(int id, string companyName, string guestPhone, DateTime checkInDate, DateTime checkOutDate, string occupancyType)
        {
            var booking = await _context.Bookings.FindAsync(id);
            if (booking == null) return NotFound();

            booking.ReceiptNumber = companyName; // اسم الشركة مخزن في ReceiptNumber
            booking.GuestPhone = guestPhone;
            booking.CheckInDate = checkInDate;
            booking.CheckOutDate = checkOutDate;
            booking.RoomType = occupancyType; // تم التصحيح هنا لاستخدام الحقل الصحيح من الموديل

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(AdminBookingsReport), new { date = checkInDate.ToString("yyyy-MM-dd") });
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