using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NileJewal.Models
{
    public class Booking
    {
        public int Id { get; set; }
        public string? RoomType { get; set; }
        public string? OccupancyType { get; set; }
        public bool IsCheckedOut { get; set; } = false; // هل تم تأكيد المغادرة الفعلية؟
        public bool IsCheckedIn { get; set; } = false; // هل تم تسكين النزيل فعلياً؟

        [Required]
        public int RoomId { get; set; }
        public Room? Room { get; set; }

        [Required(ErrorMessage = "اسم النزيل مطلوب")]
        [Display(Name = "اسم النزيل")]
        public string GuestName { get; set; }

        [Required(ErrorMessage = "رقم الموبايل مطلوب")]
        [RegularExpression(@"^01[0125][0-9]{8}$", ErrorMessage = "رقم الموبايل غير صحيح. يجب أن يكون 11 رقماً ويبدأ بـ 010 أو 011 أو 012 أو 015")]
        [Phone]
        [Display(Name = "رقم الموبايل")]
        public string GuestPhone { get; set; }

        [Required]
        [Display(Name = "تاريخ الوصول")]
        public DateTime CheckInDate { get; set; }

        [Required]
        [Display(Name = "تاريخ المغادرة")]
        public DateTime CheckOutDate { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "سعر الليلة")]
        public decimal PricePerNight { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "المبلغ الإجمالي")]
        public decimal TotalAmount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "المبلغ المدفوع")]
        public decimal PaidAmount { get; set; }

        [NotMapped]
        [Display(Name = "المبلغ المتبقي")]
        public decimal RemainingAmount => TotalAmount - PaidAmount;

        [Display(Name = "رقم الإيصال")]
        public string? ReceiptNumber { get; set; }

        [Display(Name = "ملاحظات")]
        public string? Notes { get; set; }

        public BookingStatus Status { get; set; } = BookingStatus.Confirmed;

        public string? CreatedByUserId { get; set; }
        public string? CreatedByUserName { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [Display(Name = "نوع الوجبة / الأغذية")]
        public string? MealType { get; set; } = "بدون وجبات";

        [Display(Name = "هل شامل أغذية؟")]
        public bool HasFood { get; set; } = false;

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "قيمة الأغذية اليومية")]
        public decimal DailyFoodAmount { get; set; } = 0;

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "إجمالي الأغذية")]
        public decimal TotalFoodAmount { get; set; } = 0;
    }
}