using Microsoft.AspNetCore.Http;

namespace ArabianHorseSystem.DTOs
{
    // ===== قوائم المستخدمين =====
    public class UserListDto
    {
        public int Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public bool IsApproved { get; set; }
        public bool IsActive { get; set; }
        public bool EmailConfirmed { get; set; }
        public string? ProfilePictureUrl { get; set; }
    }

    // ===== تفاصيل المستخدم للمراجعة =====
    public class UserDetailsDto
    {
        public int Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public bool IsApproved { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public string? ApprovedByName { get; set; }
        public string? ApprovalNotes { get; set; }
        
        // حسب الدور
        public SellerDetailsDto? SellerDetails { get; set; }
        public BuyerDetailsDto? BuyerDetails { get; set; }
        public VetDetailsDto? VetDetails { get; set; }
        public OwnerDetailsDto? OwnerDetails { get; set; }
        public TrainerDetailsDto? TrainerDetails { get; set; }
        
        // المستندات
        public List<UserDocumentDto> Documents { get; set; } = new();
    }

    public class SellerDetailsDto
    {
        public string NationalId { get; set; } = string.Empty;
        public string SellerType { get; set; } = string.Empty;
        public string? FarmName { get; set; }
        public string? Address { get; set; }
        public string? CommercialRegister { get; set; }
        public int ExperienceYears { get; set; }
        public string SellerRole { get; set; } = string.Empty;
    }

    public class BuyerDetailsDto
    {
        public string NationalId { get; set; } = string.Empty;
        public string? Governorate { get; set; }
    }

    public class VetDetailsDto
    {
        public string NationalId { get; set; } = string.Empty;
        public string CountryCity { get; set; } = string.Empty;
        public string LicenseNumber { get; set; } = string.Empty;
        public int ExperienceYears { get; set; }
        public string VetSpecialization { get; set; } = string.Empty;
        public string? ClinicsWorkedAt { get; set; }
        public string? VetBio { get; set; }
    }

    public class OwnerDetailsDto
    {
        public string? Preferences { get; set; }
    }

    public class TrainerDetailsDto
    {
        public string? Specialization { get; set; }
        public int? ExperienceYears { get; set; }
    }

    public class UserDocumentDto
    {
        public int Id { get; set; }
        public string DocumentType { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public DateTime UploadedAt { get; set; }
    }

    // ===== الموافقة على المستخدم =====
    public class ApproveUserDto
    {
        public int UserId { get; set; }
        public bool IsApproved { get; set; } // true = موافقة, false = رفض
        public string? Notes { get; set; } // ملاحظات أو سبب الرفض
    }

    // ===== إنشاء مستخدم بواسطة Admin =====
    public class AdminCreateUserDto
    {
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public bool IsApproved { get; set; } = true; // Admin يوافق فوراً
        public bool EmailConfirmed { get; set; } = true; // يعتبر مؤكد
        
        // اختياري
        public string? HowDidYouHear { get; set; }
    }

    // ===== تعديل مستخدم بواسطة Admin =====
    public class AdminUpdateUserDto
    {
        public string? FullName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Role { get; set; }
        public bool? IsApproved { get; set; }
        public bool? IsActive { get; set; }
        public bool? IsVerifiedBidder { get; set; }
    }

    // ===== إحصائيات لوحة التحكم =====
    public class AdminDashboardStatsDto
    {
        public int TotalUsers { get; set; }
        public int PendingApprovals { get; set; }
        public int ApprovedUsers { get; set; }
        public int RejectedUsers { get; set; }
        
        public Dictionary<string, int> UsersByRole { get; set; } = new();
        
        public int TotalAuctions { get; set; }
        public int ActiveAuctions { get; set; }
        public int TotalHorses { get; set; }
        
        public List<RecentUserDto> RecentUsers { get; set; } = new();
        public List<PendingApprovalDto> PendingUsers { get; set; } = new();
    }

    public class RecentUserDto
    {
        public int Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class PendingApprovalDto
    {
        public int Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public int DocumentsCount { get; set; }
    }
}
