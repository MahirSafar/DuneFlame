using DuneFlame.Application.Interfaces;
using DuneFlame.Infrastructure.Authentication;
using DuneFlame.Infrastructure.Configuration;
using DuneFlame.Infrastructure.Persistence;
using DuneFlame.Infrastructure.Products.Commands.UpdateProduct;
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

        // Infrastructure services
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IEmailService, SmtpEmailService>();
        services.AddSingleton(_ => Google.Cloud.Storage.V1.StorageClient.Create());
        services.AddScoped<IFileService, CloudStorageService>();
        services.AddScoped<ICurrencyProvider, CurrencyProvider>();
        services.AddScoped<ICartValidator, CartValidator>();

        // Business services
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserProfileService, UserProfileService>();
        services.AddScoped<INewsletterService, NewsletterService>();
        services.AddScoped<IContactService, ContactService>();
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IOriginService, OriginService>();
        services.AddScoped<ICartService, CartService>();
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

        // CQRS
        services.AddMediatR(config =>
            config.RegisterServicesFromAssembly(typeof(UpdateProductCommandHandler).Assembly));
        services.AddScoped<IProductUpdateStrategy, CoffeeProductUpdateStrategy>();
        services.AddScoped<IProductUpdateStrategy, EquipmentProductUpdateStrategy>();

        return services;
    }
}
