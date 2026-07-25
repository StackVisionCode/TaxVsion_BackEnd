using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Minio;
using StackExchange.Redis;
using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Application.EventMappings;
using TaxVision.Scribe.Application.Layouts;
using TaxVision.Scribe.Application.Rendering;
using TaxVision.Scribe.Application.Templates;
using TaxVision.Scribe.Application.Templates.Storage;
using TaxVision.Scribe.Infrastructure.Persistence;
using TaxVision.Scribe.Infrastructure.Persistence.Repositories;
using TaxVision.Scribe.Infrastructure.Rendering;
using TaxVision.Scribe.Infrastructure.Storage;

namespace TaxVision.Scribe.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddScribeInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString =
            configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        services.AddDbContext<ScribeDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<ScribeDbContext>());

        services.AddScoped<IEventTemplateMappingRepository, EventTemplateMappingRepository>();
        services.AddScoped<EventTemplateResolver>();

        services.AddScoped<IEmailTemplateRepository, EmailTemplateRepository>();
        services.AddScoped<IEmailLayoutRepository, EmailLayoutRepository>();
        services.AddScoped<IEmailRenderer, FluidTemplateRenderer>();

        services.AddScoped<ITenantLogoRefRepository, TenantLogoRefRepository>();
        services.AddScoped<ITenantLogoMissingNotificationRepository, TenantLogoMissingNotificationRepository>();
        services.AddScoped<ISystemAssetRefRepository, SystemAssetRefRepository>();
        services.AddScoped<ILogoResolver, LogoResolver>();

        // Preflight de publish (Fase 4.6 + 5): puro, sin estado — singleton.
        services.AddSingleton<TaxVision.Scribe.Application.Templates.Validation.EmailHtmlSafetyValidator>();

        AddRenderCache(services, configuration);
        AddCloudStorageClient(services, configuration);

        // RBAC Fase 7 (RBAC_Hardening_Plan.md) -- proyeccion local de permisos consultada por
        // ProjectionPermissionsSource cuando Authorization:PermissionsSource="Projection". La misma
        // instancia scoped satisface el puerto local rico (para los consumers) y el puerto
        // compartido y angosto de BuildingBlocks (para la autorizacion), evitando dos lecturas
        // separadas del mismo dato.
        services.AddScoped<UserPermissionsProjectionRepository>();
        services.AddScoped<IUserPermissionsProjectionRepository>(sp =>
            sp.GetRequiredService<UserPermissionsProjectionRepository>()
        );
        services.AddScoped<IUserPermissionsProjectionReader>(sp =>
            sp.GetRequiredService<UserPermissionsProjectionRepository>()
        );
        services.AddScoped<IRolePermissionsProjectionRepository, RolePermissionsProjectionRepository>();
        return services;
    }

    /// <summary>L1 (MemoryCache, LRU 1000 entries vía SizeLimit) siempre disponible; L2 (Redis) se degrada a no-op si no hay Redis configurado — mismo criterio que Postmaster.</summary>
    private static void AddRenderCache(IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache(options => options.SizeLimit = 1000);

        var redisConnectionString = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));
            services.AddSingleton<ITemplateSourceCache, RedisTemplateSourceCache>();
        }
        else
        {
            services.AddSingleton<ITemplateSourceCache, NoOpTemplateSourceCache>();
        }
    }

    private static void AddCloudStorageClient(IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<ServiceAuthClientOptions>()
            .Bind(configuration.GetSection(ServiceAuthClientOptions.SectionName));
        services
            .AddOptions<CloudStorageClientOptions>()
            .Bind(configuration.GetSection(CloudStorageClientOptions.SectionName));
        services.AddOptions<ScribeMinioOptions>().Bind(configuration.GetSection(ScribeMinioOptions.SectionName));

        services.AddHttpClient<IServiceTokenAcquirer, ServiceTokenAcquirer>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<ServiceAuthClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.AuthBaseUrl));
            }
        );

        // Sube directo a MinIO con credenciales propias de Scribe (Fase 5, patrón D1); la lectura
        // sigue via HTTP+M2M (ICloudStorageClient.DownloadTextAsync, ya existente desde Fase 4).
        services.AddSingleton<IMinioClient>(sp =>
        {
            var opt = sp.GetRequiredService<IOptions<ScribeMinioOptions>>().Value;
            var builder = new MinioClient().WithEndpoint(opt.Endpoint).WithCredentials(opt.AccessKey, opt.SecretKey);
            if (opt.UseTls)
                builder = builder.WithSSL();
            return builder.Build();
        });

        services.AddHttpClient<ICloudStorageClient, CloudStorageClient>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<CloudStorageClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.BaseUrl));
            }
        );

        services.AddScoped<ITemplateStorageService, TemplateStorageService>();
    }

    private static string NormalizeBaseUrl(string url) => url.EndsWith('/') ? url : url + "/";
}
