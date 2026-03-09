using ArabianHorseSystem.Data;
using ArabianHorseSystem.Models;
using ArabianHorseSystem.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// ========== 1. إضافة Database Context ==========
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ========== 2. إضافة Identity ==========
builder.Services.AddIdentity<User, IdentityRole<int>>(options =>
{
    // إعدادات Password
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 6;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireLowercase = false;
    
    // إعدادات User
    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedEmail = false;
    
    // Lockout settings
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// ========== 3. إضافة JWT Authentication ==========
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.SaveToken = true;
    options.RequireHttpsMetadata = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["JWT:ValidIssuer"],
        ValidAudience = builder.Configuration["JWT:ValidAudience"],
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(builder.Configuration["JWT:Secret"] ?? 
            "ArabianHorseSystemSuperSecretKey2026!ArabianHorseSystemSuperSecretKey2026!"))
    };
});

// ========== 4. إضافة Authorization مع Policies ==========
builder.Services.AddAuthorization(options =>
{
    // Policies للـ Admin
    options.AddPolicy("AdminOnly", policy => 
        policy.RequireRole("Admin"));
    
    // Policy للمستخدمين الموافق عليهم
    options.AddPolicy("ApprovedOnly", policy => 
        policy.RequireClaim("IsApproved", "True"));
    
    // Policy للمستخدمين الموثقين للمزادات
    options.AddPolicy("VerifiedBidder", policy => 
        policy.RequireClaim("IsVerifiedBidder", "True"));
    
    // Policy للبائعين فقط (طريقة 1: باستخدام RequireAssertion)
    options.AddPolicy("SellerOnly", policy =>
        policy.RequireAssertion(context =>
            context.User.HasClaim(c => c.Type == ClaimTypes.Role && 
                (c.Value == "Seller" || c.Value == "Admin"))));
    
    // Policy للبائعين فقط (طريقة 2: باستخدام RequireRole - أبسط)
    options.AddPolicy("SellerOnlySimple", policy =>
        policy.RequireRole("Seller", "Admin"));
    
    // Policy للأطباء فقط
    options.AddPolicy("VetOnly", policy => 
        policy.RequireRole("EquineVet"));
    
    // Policy للمشترين فقط
    options.AddPolicy("BuyerOnly", policy =>
        policy.RequireRole("Buyer"));
    
    // Policy للمستخدمين العاديين
    options.AddPolicy("UserOnly", policy =>
        policy.RequireRole("User"));
    
    // Policy للمستخدمين المسجلين (أي دور)
    options.AddPolicy("AuthenticatedUsers", policy =>
        policy.RequireAuthenticatedUser());
    
    // Policy متقدمة: البائعون الذين لديهم خيول نشطة
    options.AddPolicy("ActiveSeller", policy =>
        policy.RequireAssertion(context =>
            context.User.HasClaim(c => c.Type == ClaimTypes.Role && c.Value == "Seller") &&
            context.User.HasClaim(c => c.Type == "HasActiveHorses" && c.Value == "True")));
    
    // Policy متقدمة: المستخدمون الذين أكملوا ملفاتهم
    options.AddPolicy("ProfileCompleted", policy =>
        policy.RequireAssertion(context =>
            context.User.HasClaim(c => c.Type == "ProfileCompleted" && c.Value == "True")));
    
    // Policy للبائعين المميزين
    options.AddPolicy("PremiumSeller", policy =>
        policy.RequireAssertion(context =>
            context.User.HasClaim(c => c.Type == ClaimTypes.Role && c.Value == "Seller") &&
            context.User.HasClaim(c => c.Type == "SellerTier" && c.Value == "Premium")));
});

// ========== 5. تسجيل Services الخاصة بنا ==========
builder.Services.AddScoped<IFileService, FileService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IAuctionService, AuctionService>();
builder.Services.AddScoped<INotificationService, NotificationService>();

// ========== 6. إضافة Controllers ==========
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.WriteIndented = true;
    });

// ========== 7. إضافة CORS ==========
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp",
        policy =>
        {
            policy.WithOrigins(
                    "http://localhost:5173",
                    "http://localhost:3000",
                    "http://localhost:5000"
                )
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        });
    
    // سياسة CORS إضافية للإنتاج
    options.AddPolicy("ProductionPolicy",
        policy =>
        {
            policy.WithOrigins(
                    "https://yourdomain.com",
                    "https://www.yourdomain.com"
                )
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        });
});

// ========== 8. إضافة Swagger ==========
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo 
    { 
        Title = "Arabian Horse System API", 
        Version = "v1",
        Description = "API لنظام الخيول العربية",
        Contact = new OpenApiContact
        {
            Name = "Support Team",
            Email = "support@arabianhorse.com"
        }
    });
    
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] {}
        }
    });
    
    // إضافة تعليقات XML إذا وجدت
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

// ========== 9. إضافة Response Caching ==========
builder.Services.AddResponseCaching();

// ========== 10. إضافة HttpContextAccessor ==========
builder.Services.AddHttpContextAccessor();

// ========== 11. إضافة Memory Cache ==========
builder.Services.AddMemoryCache();

// ========== 12. بناء التطبيق ==========
var app = builder.Build();

// ========== 13. إعداد Middleware Pipeline ==========

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Arabian Horse API V1");
        c.RoutePrefix = string.Empty; // يجعل Swagger في المسار الرئيسي
    });
}
else
{
    app.UseHttpsRedirection();
    app.UseResponseCaching();
}

app.UseCors("AllowReactApp");
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// ========== 14. إضافة Middleware مخصص ==========
app.Use(async (context, next) =>
{
    // إضافة Headers أمنية
    context.Response.Headers.Add("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Add("X-Frame-Options", "DENY");
    context.Response.Headers.Add("X-XSS-Protection", "1; mode=block");
    
    await next();
});

app.MapControllers();

// ========== 15. Seed Database (مع تشغيل الـ Seeder) ==========
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        await context.Database.MigrateAsync();
        
        // ✅ هنا شغلي الـ Seeder (فكي التعليق)
        await DbSeeder.SeedAsync(services);
        
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("Database migrated and seeded successfully.");
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while migrating or seeding the database.");
    }
}

// ========== 16. تشغيل التطبيق ==========
app.Run();
