using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TaxVision.Campaigns.Application.Campaigns.Abstractions;
using TaxVision.Campaigns.Application.Contacts.Abstractions;
using TaxVision.Campaigns.Application.Permissions.Abstractions;
using TaxVision.Campaigns.Application.Runs.Abstractions;
using TaxVision.Campaigns.Application.Scheduling.Abstractions;
using TaxVision.Campaigns.Application.Senders.Abstractions;
using TaxVision.Campaigns.Infrastructure.Customers;
using TaxVision.Campaigns.Infrastructure.Jobs;
using TaxVision.Campaigns.Infrastructure.Permissions;
using TaxVision.Campaigns.Infrastructure.Persistence;
using TaxVision.Campaigns.Infrastructure.Persistence.Repositories;
using TaxVision.Campaigns.Infrastructure.RateLimiting;

namespace TaxVision.Campaigns.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCampaignsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString =
            configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        services.AddDbContext<CampaignsDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<CampaignsDbContext>());

        services.AddScoped<ICampaignRepository, CampaignRepository>();
        services.AddScoped<ICampaignRunRepository, CampaignRunRepository>();
        services.AddScoped<IContactRepository, ContactRepository>();
        services.AddScoped<IContactListRepository, ContactListRepository>();
        services.AddScoped<ISenderProfileRepository, SenderProfileRepository>();
        services.AddScoped<ICampaignScheduleRepository, CampaignScheduleRepository>();

        // Audiencia desde Customer (M2M): cliente HTTP al endpoint interno del directorio de clientes.
        services.AddOptions<CustomerServiceOptions>().Bind(configuration.GetSection(CustomerServiceOptions.SectionName));
        services.AddHttpClient<ICustomerAudienceClient, CustomerAudienceClient>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<CustomerServiceOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.BaseUrl));
                http.Timeout = TimeSpan.FromSeconds(15);
            }
        );

        // Scheduler durable (SendMode Scheduled/Recurring): lease-based claim + fan-out por disparo.
        services.AddHostedService<CampaignSchedulerService>();

        AddRbacProjection(services, configuration);

        return services;
    }

    /// <summary>
    /// RBAC (proyección local de permisos, patrón Notes): consumers de Auth
    /// (<c>UserRolesChanged</c>/<c>RolePermissionsChanged</c>) mantienen la proyección; el reader la
    /// consulta desde <c>ProjectionPermissionsSource</c>. Pull-recovery (Opción B): ante un miss local
    /// se recupera el snapshot desde Auth por M2M (cold-start sin re-disparar backfill).
    /// </summary>
    private static void AddRbacProjection(IServiceCollection services, IConfiguration configuration)
    {
        // Una sola instancia scoped sirve el puerto rico (consumers) y el angosto (BuildingBlocks reader).
        services.AddScoped<UserPermissionsProjectionRepository>();
        services.AddScoped<IUserPermissionsProjectionRepository>(p =>
            p.GetRequiredService<UserPermissionsProjectionRepository>()
        );
        services.AddScoped<IUserPermissionsProjectionReader>(p =>
            p.GetRequiredService<UserPermissionsProjectionRepository>()
        );
        services.AddScoped<IRolePermissionsProjectionRepository, RolePermissionsProjectionRepository>();

        // Pull-recovery: writer local + cliente HTTP al snapshot de Auth (M2M via IServiceTokenAcquirer).
        services.AddScoped<IUserPermissionsProjectionWriter, PermissionsProjectionWriter>();
        services.AddOptions<ServiceAuthClientOptions>().Bind(configuration.GetSection(ServiceAuthClientOptions.SectionName));
        services.AddHttpClient<IServiceTokenAcquirer, ServiceTokenAcquirer>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<ServiceAuthClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.AuthBaseUrl));
            }
        );
        services.AddHttpClient<IPermissionsSnapshotClient, PermissionsSnapshotClient>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<ServiceAuthClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.AuthBaseUrl));
                http.Timeout = TimeSpan.FromSeconds(15);
            }
        );
    }

    private static string NormalizeBaseUrl(string url) => url.EndsWith('/') ? url : url + "/";
}
