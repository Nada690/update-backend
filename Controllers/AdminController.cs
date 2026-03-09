using ArabianHorseSystem.Data;
using ArabianHorseSystem.DTOs;
using ArabianHorseSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ArabianHorseSystem.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin")] // فقط الـ Admin يقدر يدخل
    public class AdminController : ControllerBase
    {
        private readonly UserManager<User> _userManager;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AdminController> _logger;

        public AdminController(
            UserManager<User> userManager,
            ApplicationDbContext context,
            ILogger<AdminController> logger)
        {
            _userManager = userManager;
            _context = context;
            _logger = logger;
        }

        // ===== 1. إحصائيات لوحة التحكم =====
        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboardStats()
        {
            try
            {
                var stats = new AdminDashboardStatsDto
                {
                    TotalUsers = await _userManager.Users.CountAsync(),
                    PendingApprovals = await _userManager.Users.CountAsync(u => !u.IsApproved && u.EmailConfirmed),
                    ApprovedUsers = await _userManager.Users.CountAsync(u => u.IsApproved),
                    RejectedUsers = await _userManager.Users.CountAsync(u => u.RejectedAt != null),
                    
                    // المستخدمين حسب الدور
                    UsersByRole = new Dictionary<string, int>
                    {
                        ["Admin"] = await _userManager.Users.CountAsync(u => u.Role == "Admin"),
                        ["Owner"] = await _userManager.Users.CountAsync(u => u.Role == "Owner"),
                        ["Trainer"] = await _userManager.Users.CountAsync(u => u.Role == "Trainer"),
                        ["EquineVet"] = await _userManager.Users.CountAsync(u => u.Role == "EquineVet"),
                        ["Buyer"] = await _userManager.Users.CountAsync(u => u.Role == "Buyer"),
                        ["Seller"] = await _userManager.Users.CountAsync(u => u.Role == "Seller"),
                        ["User"] = await _userManager.Users.CountAsync(u => u.Role == "User")
                    },
                    
                    TotalAuctions = await _context.Auctions.CountAsync(),
                    ActiveAuctions = await _context.Auctions.CountAsync(a => a.Status == "Active"),
                    TotalHorses = await _context.HorseProfiles.CountAsync(),
                    
                    // آخر المستخدمين المسجلين
                    RecentUsers = await _userManager.Users
                        .OrderByDescending(u => u.CreatedAt)
                        .Take(5)
                        .Select(u => new RecentUserDto
                        {
                            Id = u.Id,
                            FullName = u.FullName ?? "",
                            Email = u.Email ?? "",
                            Role = u.Role ?? "",
                            CreatedAt = u.CreatedAt
                        })
                        .ToListAsync(),
                    
                    // المستخدمين في انتظار الموافقة
                    PendingUsers = await _userManager.Users
                        .Where(u => !u.IsApproved && u.EmailConfirmed && u.RejectedAt == null)
                        .OrderBy(u => u.CreatedAt)
                        .Take(10)
                        .Select(u => new PendingApprovalDto
                        {
                            Id = u.Id,
                            FullName = u.FullName ?? "",
                            Email = u.Email ?? "",
                            Role = u.Role ?? "",
                            CreatedAt = u.CreatedAt,
                            DocumentsCount = _context.UserDocuments.Count(d => d.UserId == u.Id)
                        })
                        .ToListAsync()
                };

                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting dashboard stats");
                return StatusCode(500, new { message = "حدث خطأ في الخادم" });
            }
        }

        // ===== 2. قائمة جميع المستخدمين =====
        [HttpGet("users")]
        public async Task<IActionResult> GetUsers(
            [FromQuery] string? role,
            [FromQuery] string? search,
            [FromQuery] bool? pending,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            try
            {
                var query = _userManager.Users.AsQueryable();

                // فلترة حسب الدور
                if (!string.IsNullOrEmpty(role))
                {
                    query = query.Where(u => u.Role == role);
                }

                // فلترة حسب البحث
                if (!string.IsNullOrEmpty(search))
                {
                    query = query.Where(u => 
                        (u.FullName != null && u.FullName.Contains(search)) ||
                        (u.Email != null && u.Email.Contains(search)) ||
                        (u.PhoneNumber != null && u.PhoneNumber.Contains(search)));
                }

                // فلترة المستخدمين في انتظار الموافقة
                if (pending == true)
                {
                    query = query.Where(u => !u.IsApproved && u.RejectedAt == null);
                }

                var totalCount = await query.CountAsync();

                var users = await query
                    .OrderByDescending(u => u.CreatedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(u => new UserListDto
                    {
                        Id = u.Id,
                        FullName = u.FullName ?? "",
                        Email = u.Email ?? "",
                        PhoneNumber = u.PhoneNumber ?? "",
                        Role = u.Role ?? "",
                        CreatedAt = u.CreatedAt,
                        IsApproved = u.IsApproved,
                        IsActive = u.IsApproved && u.EmailConfirmed,
                        EmailConfirmed = u.EmailConfirmed,
                        ProfilePictureUrl = u.ProfilePictureUrl
                    })
                    .ToListAsync();

                return Ok(new
                {
                    users,
                    totalCount,
                    page,
                    pageSize,
                    totalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting users");
                return StatusCode(500, new { message = "حدث خطأ في الخادم" });
            }
        }

        // ===== 3. تفاصيل مستخدم معين للمراجعة =====
        [HttpGet("users/{id}")]
        public async Task<IActionResult> GetUserDetails(int id)
        {
            try
            {
                var user = await _userManager.FindByIdAsync(id.ToString());
                if (user == null)
                {
                    return NotFound(new { message = "المستخدم غير موجود" });
                }

                var details = new UserDetailsDto
                {
                    Id = user.Id,
                    FullName = user.FullName ?? "",
                    Email = user.Email ?? "",
                    PhoneNumber = user.PhoneNumber ?? "",
                    Role = user.Role ?? "",
                    CreatedAt = user.CreatedAt,
                    IsApproved = user.IsApproved,
                    ApprovedAt = user.ApprovedAt,
                    ApprovalNotes = user.ApprovalNotes
                };

                // جلب المستندات
                details.Documents = await _context.UserDocuments
                    .Where(d => d.UserId == user.Id)
                    .Select(d => new UserDocumentDto
                    {
                        Id = d.Id,
                        DocumentType = d.DocumentType,
                        FileName = d.FileName,
                        FilePath = d.FilePath,
                        UploadedAt = d.UploadedAt
                    })
                    .ToListAsync();

                // جلب التفاصيل حسب الدور
                switch (user.Role)
                {
                    case "Seller":
                        var seller = await _context.SellerDetails
                            .FirstOrDefaultAsync(s => s.UserId == user.Id);
                        if (seller != null)
                        {
                            details.SellerDetails = new SellerDetailsDto
                            {
                                NationalId = seller.NationalId,
                                SellerType = seller.SellerType,
                                FarmName = seller.FarmName,
                                Address = seller.Address,
                                CommercialRegister = seller.CommercialRegister,
                                ExperienceYears = seller.ExperienceYears,
                                SellerRole = seller.SellerRole
                            };
                        }
                        break;

                    case "Buyer":
                        var buyer = await _context.BuyerDetails
                            .FirstOrDefaultAsync(b => b.UserId == user.Id);
                        if (buyer != null)
                        {
                            details.BuyerDetails = new BuyerDetailsDto
                            {
                                NationalId = buyer.NationalId,
                                Governorate = buyer.Governorate
                            };
                        }
                        break;

                    case "EquineVet":
                        var vet = await _context.VetDetails
                            .FirstOrDefaultAsync(v => v.UserId == user.Id);
                        if (vet != null)
                        {
                            details.VetDetails = new VetDetailsDto
                            {
                                NationalId = vet.NationalId,
                                CountryCity = vet.CountryCity,
                                LicenseNumber = vet.LicenseNumber,
                                ExperienceYears = vet.ExperienceYears,
                                VetSpecialization = vet.VetSpecialization,
                                ClinicsWorkedAt = vet.ClinicsWorkedAt,
                                VetBio = vet.VetBio
                            };
                        }
                        break;
                }

                return Ok(details);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user details");
                return StatusCode(500, new { message = "حدث خطأ في الخادم" });
            }
        }

        // ===== 4. الموافقة على المستخدم أو رفضه =====
        [HttpPost("users/approve")]
        public async Task<IActionResult> ApproveUser([FromBody] ApproveUserDto model)
        {
            try
            {
                var user = await _userManager.FindByIdAsync(model.UserId.ToString());
                if (user == null)
                {
                    return NotFound(new { message = "المستخدم غير موجود" });
                }

                var adminId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var admin = await _userManager.FindByIdAsync(adminId!);

                if (model.IsApproved)
                {
                    // موافقة
                    user.IsApproved = true;
                    user.ApprovedAt = DateTime.UtcNow;
                    user.ApprovedById = int.Parse(adminId!);
                    user.ApprovalNotes = model.Notes;
                    user.RejectedAt = null;
                    user.RejectionReason = null;

                    await _userManager.UpdateAsync(user);

                    // إرسال إيميل للمستخدم بالموافقة
                    // await _emailService.SendApprovalEmail(user.Email, user.FullName);
                }
                else
                {
                    // رفض
                    user.RejectedAt = DateTime.UtcNow;
                    user.RejectionReason = model.Notes ?? "لم يتم تقديم سبب";

                    await _userManager.UpdateAsync(user);

                    // إرسال إيميل للمستخدم بالرفض
                    // await _emailService.SendRejectionEmail(user.Email, user.FullName, model.Notes);
                }

                return Ok(new 
                { 
                    message = model.IsApproved ? "تمت الموافقة على المستخدم بنجاح" : "تم رفض المستخدم",
                    user.IsApproved,
                    user.ApprovedAt,
                    user.RejectedAt
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error approving user");
                return StatusCode(500, new { message = "حدث خطأ في الخادم" });
            }
        }

        // ===== 5. إنشاء مستخدم جديد بواسطة Admin =====
        [HttpPost("users")]
        public async Task<IActionResult> CreateUser([FromBody] AdminCreateUserDto model)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                // التحقق من وجود البريد
                var existingUser = await _userManager.FindByEmailAsync(model.Email);
                if (existingUser != null)
                {
                    return BadRequest(new { message = "البريد الإلكتروني موجود بالفعل" });
                }

                var user = new User
                {
                    UserName = model.Email,
                    Email = model.Email,
                    FullName = model.FullName,
                    PhoneNumber = model.PhoneNumber,
                    Role = model.Role,
                    HowDidYouHear = model.HowDidYouHear,
                    CreatedAt = DateTime.UtcNow,
                    IsApproved = model.IsApproved,
                    EmailConfirmed = model.EmailConfirmed
                };

                var result = await _userManager.CreateAsync(user, model.Password);

                if (!result.Succeeded)
                {
                    var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                    return BadRequest(new { message = errors });
                }

                // إضافة للـ Role
                await _userManager.AddToRoleAsync(user, model.Role);

                return Ok(new 
                { 
                    message = "تم إنشاء المستخدم بنجاح",
                    userId = user.Id 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating user");
                return StatusCode(500, new { message = "حدث خطأ في الخادم" });
            }
        }

        // ===== 6. تعديل مستخدم =====
        [HttpPut("users/{id}")]
        public async Task<IActionResult> UpdateUser(int id, [FromBody] AdminUpdateUserDto model)
        {
            try
            {
                var user = await _userManager.FindByIdAsync(id.ToString());
                if (user == null)
                {
                    return NotFound(new { message = "المستخدم غير موجود" });
                }

                // تحديث الحقول
                if (!string.IsNullOrEmpty(model.FullName))
                    user.FullName = model.FullName;

                if (!string.IsNullOrEmpty(model.PhoneNumber))
                    user.PhoneNumber = model.PhoneNumber;

                if (!string.IsNullOrEmpty(model.Role))
                {
                    var oldRole = user.Role;
                    user.Role = model.Role;
                    
                    // تحديث Role في Identity
                    var roles = await _userManager.GetRolesAsync(user);
                    await _userManager.RemoveFromRolesAsync(user, roles);
                    await _userManager.AddToRoleAsync(user, model.Role);
                }

                if (model.IsApproved.HasValue)
                    user.IsApproved = model.IsApproved.Value;

                if (model.IsVerifiedBidder.HasValue)
                    user.IsVerifiedBidder = model.IsVerifiedBidder.Value;

                var result = await _userManager.UpdateAsync(user);

                if (!result.Succeeded)
                {
                    var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                    return BadRequest(new { message = errors });
                }

                return Ok(new { message = "تم تحديث المستخدم بنجاح" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating user");
                return StatusCode(500, new { message = "حدث خطأ في الخادم" });
            }
        }

        // ===== 7. حذف مستخدم =====
        [HttpDelete("users/{id}")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            try
            {
                var user = await _userManager.FindByIdAsync(id.ToString());
                if (user == null)
                {
                    return NotFound(new { message = "المستخدم غير موجود" });
                }

                // منع حذف الـ Admin
                if (user.Role == "Admin")
                {
                    return BadRequest(new { message = "لا يمكن حذف حساب مدير" });
                }

                var result = await _userManager.DeleteAsync(user);

                if (!result.Succeeded)
                {
                    var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                    return BadRequest(new { message = errors });
                }

                return Ok(new { message = "تم حذف المستخدم بنجاح" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting user");
                return StatusCode(500, new { message = "حدث خطأ في الخادم" });
            }
        }

        // ===== 8. تعيين Admin جديد =====
        [HttpPost("make-admin/{id}")]
        public async Task<IActionResult> MakeAdmin(int id)
        {
            try
            {
                var user = await _userManager.FindByIdAsync(id.ToString());
                if (user == null)
                {
                    return NotFound(new { message = "المستخدم غير موجود" });
                }

                user.Role = "Admin";
                user.IsAdmin = true;
                user.IsApproved = true;

                await _userManager.UpdateAsync(user);
                
                // إضافة لـ Role Admin في Identity
                await _userManager.AddToRoleAsync(user, "Admin");

                return Ok(new { message = "تم تعيين المستخدم كمدير بنجاح" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error making admin");
                return StatusCode(500, new { message = "حدث خطأ في الخادم" });
            }
        }
    }
}
