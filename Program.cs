using ArabianHorseSystem.Data;
using ArabianHorseSystem.Models;
using ArabianHorseSystem.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;

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
    options.SignIn.RequireConfirmedEmail = false; // حالياً مش مفعلة
    
    // Lockout settings
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// ========== 3. إضافة JWT Authentication (الجزء المطلوب) ==========
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

// ========== 4. إضافة Authorization ==========
builder.Services.AddAuthorization();

// ========== 5. تسجيل Services الخاصة بنا ==========
builder.Services.AddScoped<IFileService, FileService>();
// builder.Services.AddScoped<IAuthService, AuthService>(); // معلق حالياً لأننا بنستخدم Identity مباشرة

// ========== 6. إضافة Email Service ==========
builder.Services.AddScoped<IEmailService, EmailService>();

// ========== 7. إضافة Controllers ==========
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // لتجنب الـ Cyclical references
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.WriteIndented = true;
    });

// ========== 8. إضافة CORS (للتعامل مع React) ==========
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp",
        policy =>
        {
            policy.WithOrigins(
                    "http://localhost:5173",  // Vite default
                    "http://localhost:3000",  // React default
                    "http://localhost:5000"   // Maybe API
                )
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        });
});

// ========== 9. إضافة Swagger للـ API Documentation ==========
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
    
    // إضافة JWT Authentication لـ Swagger
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
});

// ========== 10. بناء التطبيق ==========
var app = builder.Build();

// ========== 11. إعداد Middleware Pipeline ==========

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Arabian Horse API V1");
        c.RoutePrefix = string.Empty; // لتكون الصفحة الرئيسية هي Swagger
    });
}
else
{
    app.UseHttpsRedirection();
}

// استخدام CORS (مهم يكون قبل Authentication)
app.UseCors("AllowReactApp");

// Static Files (للملفات المرفوعة)
app.UseStaticFiles();

// Routing
app.UseRouting();

// Authentication & Authorization (الترتيب مهم)
app.UseAuthentication();
app.UseAuthorization();

// Map Controllers
app.MapControllers();

// ========== 12. Seed Database (اختياري) ==========
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        // للتأكد من إنشاء قاعدة البيانات
        var context = services.GetRequiredService<ApplicationDbContext>();
        await context.Database.MigrateAsync();
        
        // لو عايزة تضيف بيانات افتراضية
        // await DbSeeder.SeedAsync(services);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while migrating the database.");
    }
}

// ========== 13. تشغيل التطبيق ==========
app.Run();
