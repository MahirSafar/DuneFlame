using DuneFlame.Application.Validators;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace DuneFlame.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(config =>
            config.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));

        services.AddValidatorsFromAssemblyContaining<UpdateProfileValidator>();

        return services;
    }
}
