namespace NileJewal.Models
{
    public enum RoomType
    {
        Single = 1,  // SGL
        Double = 2,  // DBL
        Twin = 3,    // TWN
        Suite = 4
    }

    public enum BookingStatus
    {
        Confirmed = 1,   // مؤكد
        CheckedIn = 2,   // تم تسجيل الدخول
        CheckedOut = 3,  // مغادر
        Canceled = 4     // ملغي
    }
}