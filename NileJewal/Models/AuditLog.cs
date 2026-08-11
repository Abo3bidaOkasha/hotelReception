using System.ComponentModel.DataAnnotations;

namespace NileJewal.Models
{
    public class AuditLog
    {
        public int Id { get; set; }

        [Display(Name = "معرف الموظف")]
        public string UserId { get; set; }

        [Display(Name = "اسم الموظف")]
        public string UserName { get; set; }

        [Display(Name = "نوع العملية")]
        public string Action { get; set; }

        [Display(Name = "اسم الجدول")]
        public string EntityName { get; set; }

        [Display(Name = "رقم السجل")]
        public int EntityId { get; set; }

        [Display(Name = "تفاصيل التغيير")]
        public string Details { get; set; }

        [Display(Name = "الوقت والتاريخ")]
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}