using BuildingBlocks.Infrastructure.Resilience;
using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Sms.Application;
using TaxVision.Sms.Application.Abstractions;
using TaxVision.Sms.Application.Permissions.Abstractions;
using TaxVision.Sms.Infrastructure.Persistence;
using TaxVision.Sms.Infrastructure.Persistence.Repositories;
using TaxVision.Sms.Infrastructure.Providers;

namespace TaxVision.Sms.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSmsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString =
            configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        services.AddDbContext<SmsDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<SmsDbContext>());

        services.AddScoped<ISmsMessageRepository, SmsMessageRepository>();
        services.AddScoped<ISmsOptOutRepository, SmsOptOutRepository>();
        services.AddScoped<IProcessedWebhookRepository, ProcessedWebhookRepository>();

        // RBAC Fase 7 — proyección local de permisos consultada por ProjectionPermissionsSource
        // cuando Authorization:PermissionsSource="Projection". La misma instancia scoped satisface
        // el puerto local rico (para los consumers) y el puerto compartido y angosto de
        // BuildingBlocks (para la autorización), evitando dos lecturas separadas del mismo dato.
        services.AddScoped<UserPermissionsProjectionRepository>();
        services.AddScoped<IUserPermissionsProjectionRepository>(sp =>
            sp.GetRequiredService<UserPermissionsProjectionRepository>()
        );
        services.AddScoped<IUserPermissionsProjectionReader>(sp =>
            sp.GetRequiredService<UserPermissionsProjectionRepository>()
        );
        services.AddScoped<IRolePermissionsProjectionRepository, RolePermissionsProjectionRepository>();

        // Config del servicio + de proveedores (sección `Sms`).
        services.AddOptions<SmsOptions>().Bind(configuration.GetSection(SmsOptions.SectionName));
        services.AddOptions<SmsProvidersOptions>().Bind(configuration.GetSection(SmsProvidersOptions.SectionName));

        // Adapters agnósticos (keyed DI por atributo) + factory + secretos de webhook.
        services.AddSmsProviders();

        // Reintentos + circuit-breaker para las llamadas salientes a proveedores (adapter genérico).
        services.AddSingleton(_ => new HttpResiliencePipelineRegistry());
        services.AddHttpClient(nameof(Providers.Generic.GenericHttpSmsProvider), http => http.Timeout = TimeSpan.FromSeconds(30));

        return services;
    }
}
