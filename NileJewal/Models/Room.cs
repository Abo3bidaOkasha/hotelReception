using System.ComponentModel.DataAnnotations;

namespace NileJewal.Models
{
    public class Room
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(10)]
        [Display(Name = "رقم الغرفة")]
        public string RoomNumber { get; set; }

        [Display(Name = "الطابق")]
        public int Floor { get; set; }

        [Display(Name = "نوع الغرفة")]
        public RoomType Type { get; set; }

        public bool IsActive { get; set; } = true;

        public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
    }
}
