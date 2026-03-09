// Models/User.cs
using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Identity;

namespace ArabianHorseSystem.Models
{
    public class User : IdentityUser<int>  // ✅ نحتفظ بـ Identity
    {
        // الحقول الموجودة عندك 👇
        public string? FullName { get; set; }
        public string? Role { get; set; } // 'Admin', 'Owner', 'Trainer', 'EquineVet', 'Buyer', 'Seller', 'User'
        public string? Bio { get; set; }
        public string? ProfilePictureUrl { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastPasswordChangedAt { get; set; }
        
        // Auction Features
        public bool IsVerifiedBidder { get; set; } = false;
        
        // الحقول الجديدة من الكود التاني 👇
        public string? HowDidYouHear { get; set; }  // سؤال "كيف توصلت إلينا"
        
        // حالة الحساب (للأدوار اللي تحتاج مراجعة)
        public bool IsApproved { get; set; } = false;  // تمت الموافقة من الإدارة؟
        public DateTime? ApprovedAt { get; set; }      // تاريخ الموافقة
        
        // العلاقات الجديدة (البيانات الإضافية حسب الدور)
        public virtual SellerDetails? SellerDetails { get; set; }
        public virtual BuyerDetails? BuyerDetails { get; set; }
        public virtual VetDetails? VetDetails { get; set; }
        public virtual ICollection<UserDocument>? Documents { get; set; }
        
        // العلاقات الموجودة عندك
        public virtual Owner? OwnerProfile { get; set; }
        public virtual Trainer? TrainerProfile { get; set; }
        public virtual EquineVet? VetProfile { get; set; }
    }
    
    // SellerDetails (جديد)
    public class SellerDetails
    {
        [Key]
        public int Id { get; set; }
        public int UserId { get; set; }
        public virtual User User { get; set; }
        
        public string NationalId { get; set; } = string.Empty;
        public string SellerType { get; set; } = string.Empty; // 'individual' or 'institution'
        public string? FarmName { get; set; }  // للمؤسسات
        public string? Address { get; set; }   // للمؤسسات
        public string? CommercialRegister { get; set; } // للمؤسسات
        public int ExperienceYears { get; set; }
        public string SellerRole { get; set; } = string.Empty; // 'مربي', 'وسيط', 'مالك خاص'
    }
    
    // BuyerDetails (جديد)
    public class BuyerDetails
    {
        [Key]
        public int Id { get; set; }
        public int UserId { get; set; }
        public virtual User User { get; set; }
        
        public string NationalId { get; set; } = string.Empty;
        public string? Governorate { get; set; }
    }
    
    // VetDetails (جديد) - ده مختلف عن EquineVet الموجود
    public class VetDetails
    {
        [Key]
        public int Id { get; set; }
        public int UserId { get; set; }
        public virtual User User { get; set; }
        
        public string NationalId { get; set; } = string.Empty;
        public string CountryCity { get; set; } = string.Empty;
        public string LicenseNumber { get; set; } = string.Empty;
        public int ExperienceYears { get; set; }
        public string VetSpecialization { get; set; } = string.Empty;
        public string? ClinicsWorkedAt { get; set; }
        public string? VetBio { get; set; }
        public bool ConfirmAccuracy { get; set; }
    }
    
    // UserDocument (جديد) - للملفات المرفوعة
    public class UserDocument
    {
        [Key]
        public int Id { get; set; }
        public int UserId { get; set; }
        public virtual User User { get; set; }
        
        public string DocumentType { get; set; } = string.Empty; // 'NationalId', 'License', 'Recommendation', 'Certificate'
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    }
}