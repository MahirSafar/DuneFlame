using DuneFlame.Application.Interfaces;
using DuneFlame.Infrastructure.Authentication;
using DuneFlame.Infrastructure.Configuration;
using DuneFlame.Infrastructure.Persistence;
using DuneFlame.Infrastructure.Products.Commands.UpdateProduct.Strategies;
using DuneFlame.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DuneFlame.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Database
        services.AddScoped<SlowQueryInterceptor>();
        services.AddDbContext<AppDbContext>((sp, opt) =>
        {
            opt.UseNpgsql(configuration.GetConnectionString("Postgres"));
            var interceptor = sp.GetService<SlowQueryInterceptor>();
            if (interceptor != null)
                opt.AddInterceptors(interceptor);
        });

        // Caching (in-memory only — Redis has been removed)
        services.AddHybridCache();
        services.AddDistributedMemoryCache();

        // Settings
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.Configure<EmailSettings>(configuration.GetSection("EmailSettings"));
        services.Configure<StripeSettings>(configuration.GetSection(StripeSettings.SectionName));
        services.Configure<ClientUrls>(configuration.GetSection(ClientUrls.SectionName));
        services.Configure<GoogleMerchantSettings>(configuration.GetSection(GoogleMerchantSettings.SectionName));
        services.Configure<QuiqupSettings>(configuration.GetSection(QuiqupSettings.SectionName));

        // Infrastructure services
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IEmailService, SmtpEmailService>();
        services.AddSingleton(_ => Google.Cloud.Storage.V1.StorageClient.Create());
        services.AddScoped<IFileService, CloudStorageService>();
        services.AddScoped<ICurrencyProvider, CurrencyProvider>();


        // Business services
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserProfileService, UserProfileService>();
        services.AddScoped<INewsletterService, NewsletterService>();
        services.AddScoped<IContactService, ContactService>();
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IOriginService, OriginService>();
        services.AddScoped<IBasketService, BasketService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IRewardService, RewardService>();
        services.AddScoped<IPaymentService, StripePaymentService>();
        services.AddScoped<IAdminUserService, AdminUserService>();
        services.AddScoped<IAdminContentService, AdminContentService>();
        services.AddScoped<IAdminOrderService, AdminOrderService>();
        services.AddScoped<IAdminDashboardService, AdminDashboardService>();
        services.AddScoped<IShippingService, ShippingService>();
        services.AddScoped<ISliderService, SliderService>();
        services.AddScoped<IWholesaleService, WholesaleService>();
        services.AddScoped<ISitemapService, SitemapService>();
        services.AddScoped<IGoogleMerchantService, GoogleMerchantService>();

        // Quiqup Last-Mile Delivery
        services.AddHttpClient<IQuiqupDeliveryService, QuiqupDeliveryService>(client =>
        {
            var baseUrl = configuration["Quiqup:BaseUrl"] ?? "https://api-ae.quiqup.com";
            client.BaseAddress = new Uri(baseUrl);
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            // Quiqup production API (api-ae.quiqup.com) uses a certificate chain that
            // HttpClient cannot fully validate in some hosting environments (Cloud Run, etc.).
            // This callback bypasses the OS-level SSL validation exclusively for Quiqup requests.
            // All other HttpClients in the application are NOT affected.
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        });

        // Quiqup webhook HMAC-SHA1 signature verifier (singleton — stateless, reads from IOptions)
        services.AddSingleton<IQuiqupSignatureVerifier, QuiqupSignatureVerifier>();

        // CQRS — all handlers are in the Application assembly (registered in Application DI)
        // Product update strategies (infrastructure-specific, injected into ProductService)
        services.AddScoped<IProductUpdateStrategy, CoffeeProductUpdateStrategy>();
        services.AddScoped<IProductUpdateStrategy, EquipmentProductUpdateStrategy>();

        return services;
    }
}
