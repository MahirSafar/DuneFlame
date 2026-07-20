using DuneFlame.API.Middlewares;
using DuneFlame.Application;
using DuneFlame.Domain.Entities;
using DuneFlame.Infrastructure;
using DuneFlame.Infrastructure.Authentication;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OAuth.Claims;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Versioning;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// --- 1. LOGGING ---
builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

// --- 2. APPLICATION & INFRASTRUCTURE ---
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// --- 3. IDENTITY ---
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

// --- 5. AUTHENTICATION & CORS ---
var jwtSettings = new JwtSettings();
builder.Configuration.Bind(JwtSettings.SectionName, jwtSettings);

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
    googleOptions.ClaimActions.MapJsonKey("urn:google:picture", "picture", "url");
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("NextJsPolicy", policy =>
    {
        policy.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:3000"])
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Allow HTTPS headers from all external Cloud Run proxies:
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.MinimumSameSitePolicy = SameSiteMode.Lax;
    options.Secure = CookieSecurePolicy.SameAsRequest;
});

// --- 6. MVC / API ---
builder.Services.AddHttpContextAccessor();
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });
builder.Services.AddOpenApi();
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new Microsoft.AspNetCore.Mvc.ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
    options.ApiVersionReader = new UrlSegmentApiVersionReader();
});

builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("Postgres") ?? "", name: "db");

// ==========================================
var app = builder.Build();

if (!app.Environment.IsEnvironment("Testing"))
{
    // Run DB migrations and seeding synchronously before the app starts serving traffic.
    // This guarantees all tables and seed data exist before any request is handled.
    // Cloud Run startup probe should allow enough time (set --startup-probe-timeout-seconds=120
    // or use --min-instances=1 with a generous --startup-cpu-boost flag).
    try
    {
        await DbInitializer.InitializeAsync(app.Services);
        Log.Information("Database initialization completed successfully.");
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "Database initialization failed. Shutting down to prevent serving requests against an uninitialised database.");
        await app.StopAsync();
        return;
    }
}

app.UseSerilogRequestLogging();
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "DuneFlame API v1"));
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.Combine(app.Environment.ContentRootPath, "wwwroot", "uploads")),
    RequestPath = "/api/v1/uploads"
});

app.UseCors("NextJsPolicy");

if (!app.Environment.IsEnvironment("Testing"))
    app.UseRateLimiter();

app.UseCookiePolicy();
app.UseAuthentication();
app.UseAuthorization();

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



