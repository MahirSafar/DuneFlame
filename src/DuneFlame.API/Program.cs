using DuneFlame.API.Middlewares;
using DuneFlame.Application.Interfaces;
using DuneFlame.Application.Validators;
using DuneFlame.Domain.Entities;
using DuneFlame.Infrastructure.Authentication;
using DuneFlame.Infrastructure.Persistence;
using DuneFlame.Infrastructure.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Versioning;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// --- 1. LOGGING (SERILOG) ---
builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

// --- 2. DATABASE SETUP ---
builder.Services.AddScoped<SlowQueryInterceptor>();
builder.Services.AddDbContext<AppDbContext>((serviceProvider, opt) =>
{
    var slowQueryInterceptor = serviceProvider.GetService<SlowQueryInterceptor>();
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Postgres"));
    if (slowQueryInterceptor != null)
    {
        opt.AddInterceptors(slowQueryInterceptor);
    }
});

// --- 2.1 CACHING (REDIS OR IN-MEMORY) ---
builder.Services.AddStackExchangeRedisCache(options =>
{
    var redisConnection = builder.Configuration.GetConnectionString("Redis");
    if (!string.IsNullOrEmpty(redisConnection))
    {
        options.Configuration = redisConnection;
    }
});

if (string.IsNullOrEmpty(builder.Configuration.GetConnectionString("Redis")))
{
    builder.Services.AddDistributedMemoryCache();
}

// --- 3. IDENTITY & SECURITY ---
builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 8;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedEmail = true; 
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

// --- 4. RATE LIMITING ---
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("AuthPolicy", opt => { opt.PermitLimit = 50; opt.Window = TimeSpan.FromMinutes(1); });
    options.AddFixedWindowLimiter("CheckoutPolicy", opt => { opt.PermitLimit = 20; opt.Window = TimeSpan.FromMinutes(1); });
    options.AddFixedWindowLimiter("ContactPolicy", opt => { opt.PermitLimit = 10; opt.Window = TimeSpan.FromMinutes(1); });
    options.AddFixedWindowLimiter("PublicPolicy", opt => { opt.PermitLimit = 100; opt.Window = TimeSpan.FromMinutes(1); });
});

// --- 5. DEPENDENCY INJECTION (BÜTÜN SERVİSLƏR) ---
var jwtSettings = new JwtSettings();
builder.Configuration.Bind(JwtSettings.SectionName, jwtSettings);
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));
builder.Services.Configure<StripeSettings>(builder.Configuration.GetSection(StripeSettings.SectionName));

// İnfrastruktur Servisləri (Xətanın həlli buradadır)
builder.Services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddScoped<IFileService, LocalFileService>();

// Biznes Servisləri
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserProfileService, UserProfileService>();
builder.Services.AddScoped<INewsletterService, NewsletterService>();
builder.Services.AddScoped<IContactService, ContactService>();
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IOriginService, OriginService>();
builder.Services.AddScoped<ICartService, CartService>();
builder.Services.AddScoped<IBasketService, BasketService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IRewardService, RewardService>();
builder.Services.AddScoped<IPaymentService, StripePaymentService>();
builder.Services.AddScoped<IAdminUserService, AdminUserService>();
builder.Services.AddScoped<IAdminContentService, AdminContentService>();
builder.Services.AddScoped<IAdminOrderService, AdminOrderService>();
builder.Services.AddScoped<IAdminDashboardService, AdminDashboardService>();

builder.Services.AddValidatorsFromAssemblyContaining<UpdateProfileValidator>();

// --- 6. AUTHENTICATION & CORS ---
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidAudience = jwtSettings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret))
    };
})
.AddGoogle(googleOptions =>
{
    googleOptions.ClientId = builder.Configuration["GoogleSettings:ClientId"]!;
    googleOptions.ClientSecret = builder.Configuration["GoogleSettings:ClientSecret"]!;
    googleOptions.CallbackPath = "/signin-google";
});

// CORS Siyasəti
builder.Services.AddCors(options =>
{
    options.AddPolicy("NextJsPolicy", policy =>
    {
        policy.WithOrigins("http://localhost:3000") 
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// --- COOKIE POLICY (FIX FOR OAUTH CORRELATION FAILURES) ---
// Relaxed SameSite policy for localhost development with mixed protocols (http/https)
builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.MinimumSameSitePolicy = SameSiteMode.Lax;
    options.Secure = CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new Microsoft.AspNetCore.Mvc.ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
    options.ApiVersionReader = new UrlSegmentApiVersionReader();
});

// --- 7. HEALTH CHECKS ---
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("Postgres") ?? "", name: "db")
    .AddRedis(builder.Configuration.GetConnectionString("Redis") ?? "", name: "redis");

// ==========================================
// === BUILD APP (MIDDLEWARE BAŞLAYIR) ===
// ==========================================
var app = builder.Build();

// Database Seeding
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    await DbInitializer.InitializeAsync(scope.ServiceProvider);
}

app.UseSerilogRequestLogging();
// Middleware Sıralaması (Kritik!)
app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "DuneFlame API v1"));
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// CORS Auth-dan qabaq gəlməlidir
app.UseCors("NextJsPolicy");

if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseRateLimiter();
}

app.UseCookiePolicy();
app.UseAuthentication();
app.UseAuthorization();

// Health Check Endpoint
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status200OK
    },
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var response = new { status = report.Status.ToString(), checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString() }) };
        await context.Response.WriteAsJsonAsync(response);
    }
});

app.MapControllers();

app.Run();

public partial class Program { }