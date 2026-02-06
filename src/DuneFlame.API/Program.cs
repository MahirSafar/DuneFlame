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
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Versioning;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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

// --- 2.1 CACHING (HYBRIDCACHE WITH REDIS L2 BACKING STORE) ---
var redisConnection = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrEmpty(redisConnection))
{
    // Configure HybridCache with Redis as L2 backing store
    builder.Services.AddHybridCache();

    // Add StackExchangeRedis for distributed cache (L2 backing store)
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnection;
        options.InstanceName = "DuneFlame_";
    });
}
else
{
    // Fallback: HybridCache with in-memory L2 store
    builder.Services.AddHybridCache();
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

// === CRITICAL: HTTP CONTEXT & CURRENCY PROVIDER ===
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrencyProvider, CurrencyProvider>();
builder.Services.AddScoped<ICartValidator, CartValidator>();

// İnfrastruktur Servisləri (Xətanın həlli buradadır)
builder.Services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddScoped<IFileService, CloudStorageService>();

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
builder.Services.AddScoped<IShippingService, ShippingService>();
builder.Services.AddScoped<ISliderService, SliderService>();

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
        policy.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? new[] { "http://localhost:3000" })
            .AllowAnyMethod()
            .AllowAnyHeader()
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

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
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
    try
    {
        using var scope = app.Services.CreateScope();
        await DbInitializer.InitializeAsync(scope.ServiceProvider);
        Log.Information("Database initialization completed successfully.");
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "Database initialization failed. The application may not have access to the database.");
        // In cloud environments, we allow the app to continue rather than crash immediately
        // This prevents cascading failures in environments like Google Cloud Run
        // where containers restart frequently during deployment
        if (!app.Environment.IsProduction())
        {
            throw; // Re-throw in development to catch issues early
        }
    }
}

app.UseSerilogRequestLogging();
// Middleware Sıralaması (Kritik!)
app.UseMiddleware<GlobalExceptionMiddleware>();

// ForwardedHeaders must be early in the pipeline to properly handle X-Forwarded-Proto, X-Forwarded-For, etc.
// This is essential for cloud environments like Cloud Run where requests come through reverse proxies
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "DuneFlame API v1"));
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// Configure static files for uploads directory
// This allows serving uploaded images (sliders, products, etc.) from the /api/v1/uploads endpoint
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.Combine(app.Environment.ContentRootPath, "wwwroot", "uploads")),
    RequestPath = "/api/v1/uploads"
});

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