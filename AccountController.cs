using ArabianHorseSystem.DTOs;
using ArabianHorseSystem.Models;
using ArabianHorseSystem.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace ArabianHorseSystem.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AccountController : ControllerBase
    {
        private readonly UserManager<User> _userManager;
        private readonly SignInManager<User> _signInManager;
        private readonly IConfiguration _configuration;
        private readonly IEmailService _emailService;
        private readonly IFileService _fileService;
        private readonly ILogger<AccountController> _logger;

        public AccountController(
            UserManager<User> userManager,
            SignInManager<User> signInManager,
            IConfiguration configuration,
            IEmailService emailService,
            IFileService fileService,
            ILogger<AccountController> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _configuration = configuration;
            _emailService = emailService;
            _fileService = fileService;
            _logger = logger;
        }

        #regionRegistration & Login

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromForm] RegisterDto model)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                // 1. التحقق من عدم وجود البريد الإلكتروني
                var existingUser = await _userManager.FindByEmailAsync(model.Email);
                if (existingUser != null)
                {
                    return BadRequest(new[] { new { description = "البريد الإلكتروني موجود بالفعل" } });
                }

                // 2. إنشاء المستخدم الأساسي
                var user = new User
                {
                    UserName = model.Email,
                    Email = model.Email,
                    FullName = model.FullName,
                    PhoneNumber = model.PhoneNumber,
                    Role = model.Role,
                    HowDidYouHear = model.HowDidYouHear,
                    CreatedAt = DateTime.UtcNow,
                    
                    // تحديد حالة الحساب حسب الدور
                    IsApproved = model.Role == "User" || model.Role == "Buyer",
                    EmailConfirmed = false // محتاج تأكيد إيميل
                };

                // 3. إنشاء المستخدم في Identity
                var result = await _userManager.CreateAsync(user, model.Password);

                if (!result.Succeeded)
                {
                    var errors = result.Errors.Select(e => new { description = e.Description });
                    return BadRequest(errors);
                }

                // 4. إضافة الـ Role في Identity (اختياري)
                await _userManager.AddToRoleAsync(user, model.Role);

                // 5. إضافة التفاصيل حسب الدور
                using var transaction = await _userManager.CreateTransaction(); // لو عندك Transactions
                
                try
                {
                    switch (model.Role)
                    {
                        case "Seller":
                            var sellerDetails = new SellerDetails
                            {
                                UserId = user.Id,
                                NationalId = model.NationalId,
                                SellerType = model.SellerType,
                                FarmName = model.FarmName,
                                Address = model.Address,
                                CommercialRegister = model.CommercialRegister,
                                ExperienceYears = model.ExperienceYears ?? 0,
                                SellerRole = model.SellerRole
                            };
                            // حفظ في قاعدة البيانات (اعملي DbSet مخصوص)
                            // _context.SellerDetails.Add(sellerDetails);
                            break;

                        case "Buyer":
                            var buyerDetails = new BuyerDetails
                            {
                                UserId = user.Id,
                                NationalId = model.NationalId,
                                Governorate = model.Governorate
                            };
                            // _context.BuyerDetails.Add(buyerDetails);
                            break;

                        case "EquineVet":
                            var vetDetails = new VetDetails
                            {
                                UserId = user.Id,
                                NationalId = model.NationalId,
                                CountryCity = model.CountryCity,
                                LicenseNumber = model.LicenseNumber,
                                ExperienceYears = model.ExperienceYears ?? 0,
                                VetSpecialization = model.VetSpecialization,
                                ClinicsWorkedAt = model.ClinicsWorkedAt,
                                VetBio = model.VetBio,
                                ConfirmAccuracy = model.ConfirmAccuracy
                            };
                            // _context.VetDetails.Add(vetDetails);
                            break;
                    }

                    // 6. حفظ الملفات
                    if (model.NationalIdFile != null)
                    {
                        var filePath = await _fileService.SaveFileAsync(
                            model.NationalIdFile,
                            user.Id.ToString(),
                            "NationalId"
                        );

                        // حفظ معلومات الملف في قاعدة البيانات
                        // _context.UserDocuments.Add(new UserDocument
                        // {
                        //     UserId = user.Id,
                        //     DocumentType = "NationalId",
                        //     FileName = model.NationalIdFile.FileName,
                        //     FilePath = filePath
                        // });
                    }

                    if (model.Role == "Seller" && model.RecommendationLetter != null)
                    {
                        var filePath = await _fileService.SaveFileAsync(
                            model.RecommendationLetter,
                            user.Id.ToString(),
                            "Recommendation"
                        );
                        // حفظ في قاعدة البيانات
                    }

                    if (model.Role == "EquineVet")
                    {
                        if (model.LicenseFile != null)
                        {
                            var filePath = await _fileService.SaveFileAsync(
                                model.LicenseFile,
                                user.Id.ToString(),
                                "License"
                            );
                            // حفظ في قاعدة البيانات
                        }

                        if (model.VetCertificates != null)
                        {
                            var filePath = await _fileService.SaveFileAsync(
                                model.VetCertificates,
                                user.Id.ToString(),
                                "Certificate"
                            );
                            // حفظ في قاعدة البيانات
                        }
                    }

                    // 7. حفظ كل التغييرات
                    // await _context.SaveChangesAsync();
                    // await transaction.CommitAsync();

                    // 8. إرسال إيميل التأكيد
                    await SendConfirmationEmail(user);

                    return Ok(new
                    {
                        message = "تم التسجيل بنجاح",
                        userId = user.Id,
                        requiresApproval = model.Role == "Seller" || model.Role == "EquineVet",
                        emailConfirmed = false
                    });
                }
                catch (Exception ex)
                {
                    // await transaction.RollbackAsync();
                    _logger.LogError(ex, "Error saving user details");
                    
                    // حذف المستخدم لو حصل خطأ
                    await _userManager.DeleteAsync(user);
                    
                    return StatusCode(500, new[] { new { description = "حدث خطأ أثناء حفظ البيانات" } });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Registration error");
                return StatusCode(500, new[] { new { description = "حدث خطأ في الخادم" } });
            }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto model)
        {
            try
            {
                var user = await _userManager.FindByEmailAsync(model.Email);
                if (user == null)
                {
                    return Unauthorized(new[] { new { description = "البريد الإلكتروني أو كلمة المرور غير صحيحة" } });
                }

                // التحقق من كلمة المرور
                var result = await _signInManager.CheckPasswordSignInAsync(user, model.Password, false);

                if (!result.Succeeded)
                {
                    return Unauthorized(new[] { new { description = "البريد الإلكتروني أو كلمة المرور غير صحيحة" } });
                }

                // التحقق من تفعيل الحساب
                if (!user.IsApproved)
                {
                    return Unauthorized(new[] { new { description = "الحساب غير مفعل. يرجى انتظار الموافقة من الإدارة" } });
                }

                // التحقق من تأكيد الإيميل (اختياري)
                if (!user.EmailConfirmed)
                {
                    return Unauthorized(new[] { new { description = "يرجى تأكيد البريد الإلكتروني أولاً" } });
                }

                // إنشاء التوكن
                var token = await GenerateJwtToken(user);

                return Ok(new
                {
                    token,
                    user = new
                    {
                        user.Id,
                        user.FullName,
                        user.Email,
                        user.Role,
                        user.ProfilePictureUrl,
                        user.IsVerifiedBidder
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Login error");
                return StatusCode(500, new[] { new { description = "حدث خطأ في الخادم" } });
            }
        }

        private async Task<string> GenerateJwtToken(User user)
        {
            var jwtSettings = _configuration.GetSection("Jwt");
            var secret = jwtSettings["Key"] ?? "ArabianHorseSystemSuperSecretKey2026!";
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email!),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, user.Role ?? ""),
                new Claim("FullName", user.FullName ?? ""),
                new Claim("UserId", user.Id.ToString())
            };

            // إضافة Roles من Identity
            var roles = await _userManager.GetRolesAsync(user);
            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var token = new JwtSecurityToken(
                issuer: jwtSettings["Issuer"],
                audience: jwtSettings["Audience"],
                claims: claims,
                expires: DateTime.Now.AddDays(7),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        #endregion

        #region Password Management

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto model)
        {
            try
            {
                var user = await _userManager.FindByEmailAsync(model.Email);
                if (user == null)
                {
                    // لأمان، لا تخبر المستخدم بوجود الإيميل أو لا
                    return Ok(new { message = "إذا كان بريدك الإلكتروني مسجلاً، ستتصل بك رسالة إعادة التعيين." });
                }

                // التأكد من وجود SecurityStamp
                if (await _userManager.GetSecurityStampAsync(user) == null)
                {
                    await _userManager.UpdateSecurityStampAsync(user);
                }

                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                
                // رابط إعادة التعيين (للـ Frontend)
                var resetLink = $"http://localhost:5173/reset-password?email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(token)}";
                
                var emailBody = $@"
                    <div dir='rtl' style='font-family: Arial, sans-serif; line-height: 1.6;'>
                        <h2 style='color: #48B02C;'>نظام الخيل العربية</h2>
                        <p>مرحباً {user.FullName}،</p>
                        <p>لقد تلقينا طلباً لإعادة تعيين كلمة المرور الخاصة بك.</p>
                        <a href='{resetLink}' style='display: inline-block; padding: 12px 24px; background-color: #48B02C; color: white; text-decoration: none; border-radius: 8px; font-weight: bold;'>
                            إعادة تعيين كلمة المرور
                        </a>
                        <p>إذا لم تطلب هذا، يمكنك تجاهل هذا البريد الإلكتروني.</p>
                    </div>";

                await _emailService.SendEmailAsync(user.Email!, "إعادة تعيين كلمة المرور", emailBody);

                return Ok(new { message = "تم إرسال رابط إعادة التعيين إلى بريدك الإلكتروني" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Forgot password error");
                return StatusCode(500, new { message = "حدث خطأ في الخادم" });
            }
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto model)
        {
            try
            {
                var user = await _userManager.FindByEmailAsync(model.Email);
                if (user == null)
                {
                    return BadRequest(new[] { new { description = "طلب غير صالح" } });
                }

                var result = await _userManager.ResetPasswordAsync(user, model.Token, model.NewPassword);

                if (result.Succeeded)
                {
                    user.LastPasswordChangedAt = DateTime.UtcNow;
                    await _userManager.UpdateAsync(user);
                    
                    return Ok(new { message = "تم إعادة تعيين كلمة المرور بنجاح" });
                }

                var errors = result.Errors.Select(e => new { description = e.Description });
                return BadRequest(errors);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reset password error");
                return StatusCode(500, new[] { new { description = "حدث خطأ في الخادم" } });
            }
        }

        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto model)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                if (user == null)
                {
                    return Unauthorized(new[] { new { description = "يجب تسجيل الدخول أولاً" } });
                }

                var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);

                if (result.Succeeded)
                {
                    user.LastPasswordChangedAt = DateTime.UtcNow;
                    await _userManager.UpdateAsync(user);
                    
                    return Ok(new { message = "تم تغيير كلمة المرور بنجاح" });
                }

                var errors = result.Errors.Select(e => new { description = e.Description });
                return BadRequest(errors);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Change password error");
                return StatusCode(500, new[] { new { description = "حدث خطأ في الخادم" } });
            }
        }

        #endregion

        #region Email Confirmation

        [HttpPost("send-confirmation")]
        public async Task<IActionResult> SendConfirmationEmail([FromBody] EmailDto model)
        {
            try
            {
                var user = await _userManager.FindByEmailAsync(model.Email);
                if (user == null)
                {
                    return Ok(new { message = "إذا كان البريد مسجلاً، ستتصل بك رسالة التأكيد" });
                }

                await SendConfirmationEmail(user);
                
                return Ok(new { message = "تم إرسال رابط التأكيد إلى بريدك الإلكتروني" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Send confirmation error");
                return StatusCode(500, new { message = "حدث خطأ في الخادم" });
            }
        }

        private async Task SendConfirmationEmail(User user)
        {
            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            var confirmationLink = $"http://localhost:5173/confirm-email?email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(token)}";
            
            var emailBody = $@"
                <div dir='rtl' style='font-family: Arial, sans-serif; line-height: 1.6;'>
                    <h2 style='color: #48B02C;'>نظام الخيل العربية</h2>
                    <p>مرحباً {user.FullName}،</p>
                    <p>شكراً لتسجيلك في منصتنا. يرجى تأكيد بريدك الإلكتروني بالنقر على الرابط أدناه:</p>
                    <a href='{confirmationLink}' style='display: inline-block; padding: 12px 24px; background-color: #48B02C; color: white; text-decoration: none; border-radius: 8px; font-weight: bold;'>
                        تأكيد البريد الإلكتروني
                    </a>
                </div>";

            await _emailService.SendEmailAsync(user.Email!, "تأكيد البريد الإلكتروني", emailBody);
        }

        [HttpGet("confirm-email")]
        public async Task<IActionResult> ConfirmEmail(string email, string token)
        {
            try
            {
                var user = await _userManager.FindByEmailAsync(email);
                if (user == null)
                {
                    return BadRequest(new { message = "طلب غير صالح" });
                }

                var result = await _userManager.ConfirmEmailAsync(user, token);
                
                if (result.Succeeded)
                {
                    return Ok(new { message = "تم تأكيد البريد الإلكتروني بنجاح" });
                }

                return BadRequest(new { message = "فشل تأكيد البريد الإلكتروني" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Confirm email error");
                return StatusCode(500, new { message = "حدث خطأ في الخادم" });
            }
        }

        #endregion

        #region Utilities

        [HttpGet("check-email")]
        public async Task<IActionResult> CheckEmail(string email)
        {
            var user = await _userManager.FindByEmailAsync(email);
            return Ok(new { exists = user != null });
        }

        [HttpGet("profile")]
        public async Task<IActionResult> GetProfile()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Unauthorized();
            }

            return Ok(new
            {
                user.Id,
                user.FullName,
                user.Email,
                user.PhoneNumber,
                user.Role,
                user.ProfilePictureUrl,
                user.IsVerifiedBidder,
                user.CreatedAt,
                user.IsApproved
            });
        }

        #endregion
    }
}