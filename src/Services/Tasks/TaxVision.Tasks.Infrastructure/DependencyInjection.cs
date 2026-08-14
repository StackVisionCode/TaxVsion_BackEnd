using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using BuildingBlocks.RateLimiting;
using BuildingBlocks.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Attachments.Abstractions;
using TaxVision.Tasks.Application.Backfill;
using TaxVision.Tasks.Application.Backfill.Abstractions;
using TaxVision.Tasks.Application.ClientRequests;
using TaxVision.Tasks.Application.ClientRequests.Abstractions;
using TaxVision.Tasks.Application.Common.Abstractions;
using TaxVision.Tasks.Application.Counters;
using TaxVision.Tasks.Application.Counters.Abstractions;
using TaxVision.Tasks.Application.Customers.Abstractions;
using TaxVision.Tasks.Application.Dependencies;
using TaxVision.Tasks.Application.Dependencies.Abstractions;
using TaxVision.Tasks.Application.Hierarchy;
using TaxVision.Tasks.Application.Hierarchy.Abstractions;
using TaxVision.Tasks.Application.Labels.Abstractions;
using TaxVision.Tasks.Application.Permissions.Abstractions;
using TaxVision.Tasks.Application.Projections.Abstractions;
using TaxVision.Tasks.Application.RateLimiting.Abstractions;
using TaxVision.Tasks.Application.Series;
using TaxVision.Tasks.Application.Series.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Application.Templates;
using TaxVision.Tasks.Application.Templates.Abstractions;
using TaxVision.Tasks.Application.Timers.Abstractions;
using TaxVision.Tasks.Infrastructure.CloudStorage;
using TaxVision.Tasks.Infrastructure.Customers;
using TaxVision.Tasks.Infrastructure.Jobs;
using TaxVision.Tasks.Infrastructure.Observability;
using TaxVision.Tasks.Infrastructure.Permissions;
using TaxVision.Tasks.Infrastructure.Persistence;
using TaxVision.Tasks.Infrastructure.Persistence.Repositories;
using TaxVision.Tasks.Infrastructure.RateLimiting;

namespace TaxVision.Tasks.Infrastructure;

/// <summary>
/// Wiring de infraestructura: el <see cref="TasksDbContext"/> con su <see cref="IUnitOfWork"/>, los
/// repositorios del dominio, las proyecciones de permisos y de plan, y el único acquirer M2M.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddTasksInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString =
            configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        services.AddDbContext<TasksDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<TasksDbContext>());
        services.AddScoped<ITaskRepository, TaskRepository>();
        services.AddScoped<ITaskDependencyRepository, TaskDependencyRepository>();
        services.AddScoped<ITaskLabelRepository, TaskLabelRepository>();
        services.AddScoped<ITaskSeriesRepository, TaskSeriesRepository>();
        services.AddScoped<ITaskTemplateRepository, TaskTemplateRepository>();
        services.AddScoped<IClientRequestRepository, ClientRequestRepository>();
        services.AddOptions<ClientReminderOptions>().Bind(configuration.GetSection(ClientReminderOptions.SectionName));
        services.AddSingleton<ITaskMetrics, TaskMetrics>();
        services.AddScoped<ITaskTimerRepository, TaskTimerRepository>();
        services.AddScoped<ITransactionalScope, TransactionalScope>();
        services.AddScoped<ITaskUnblockingService, TaskUnblockingService>();
        services.AddScoped<ITaskHierarchyService, TaskHierarchyService>();
        services.AddScoped<ITaskSeriesMaterializer, TaskSeriesMaterializer>();
        services.AddScoped<ITaskTemplateInstantiator, TaskTemplateInstantiator>();
        services.AddScoped<ICounterReconciler, CounterReconciler>();
        services.AddHostedService<CounterReconciliationJob>();
        services.AddHostedService<SeriesMaterializationJob>();

        AddPermissionsProjections(services);
        AddRateLimitTierQuotas(services, configuration);
        AddPermissionsPullRecovery(services);
        AddCustomerDirectory(services, configuration);

        return services;
    }

    /// <summary>
    /// El veredicto del escaneo se publica una sola vez: un adjunto creado después de esa publicación
    /// no vuelve a recibirlo. Este cliente pregunta el estado y el job cierra los que se quedaron
    /// colgados.
    /// </summary>
    private static void AddAttachmentScanReconciliation(IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<CloudStorageClientOptions>()
            .Bind(configuration.GetSection(CloudStorageClientOptions.SectionName));

        services.AddHttpClient<ITaskFileScanStatusClient, TasksFileScanStatusClient>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<CloudStorageClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.BaseUrl));
                http.Timeout = TimeSpan.FromSeconds(15);
            }
        );

        services
            .AddOptions<StaleAttachmentOptions>()
            .Bind(configuration.GetSection(StaleAttachmentOptions.SectionName));
        services.AddScoped<IStaleAttachmentResolver, StaleAttachmentResolver>();
        services.AddHostedService<StaleAttachmentJob>();

        services.AddOptions<TaskRetentionOptions>().Bind(configuration.GetSection(TaskRetentionOptions.SectionName));
        services.AddHostedService<TaskRetentionJob>();

        services
            .AddOptions<OverdueTaskSweepOptions>()
            .Bind(configuration.GetSection(OverdueTaskSweepOptions.SectionName));
        services.AddHostedService<OverdueTaskSweepJob>();
    }

    /// <summary>
    /// Directorio local de customers: repositorios, backfill reactivo, cliente M2M hacia Customer y
    /// los dos jobs. El cliente reusa el <see cref="IServiceTokenAcquirer"/> del servicio y sólo suma
    /// un HttpClient tipado hacia otro destino.
    /// </summary>
    private static void AddCustomerDirectory(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICustomerDirectoryRepository, CustomerDirectoryRepository>();
        services.AddScoped<ITenantBackfillStateRepository, TenantBackfillStateRepository>();
        services.AddScoped<ITenantCustomerBackfillService, TenantCustomerBackfillService>();

        services.AddOptions<CustomerClientOptions>().Bind(configuration.GetSection(CustomerClientOptions.SectionName));
        services.AddHttpClient<ITasksCustomerClient, TasksCustomerClient>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<CustomerClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.BaseUrl));
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );

        services
            .AddOptions<CustomerDirectoryReconciliationOptions>()
            .Bind(configuration.GetSection(CustomerDirectoryReconciliationOptions.SectionName));

        // Uno rellena nombres faltantes de filas que ya existen; el otro inserta las filas que nunca
        // llegaron. Son huecos distintos y ninguno cubre al otro.
        services.AddHostedService<CustomerDirectoryReconciliationJob>();
        services.AddHostedService<TenantCustomerFullReconciliationJob>();

        AddAttachmentScanReconciliation(services, configuration);
    }

    /// <summary>
    /// Escalado de cuotas por tier. Lo que se registra acá está siempre activo: el consumer del
    /// evento de Subscription mantiene <c>TenantPlanCodeProjections</c> al día aunque el flag esté
    /// apagado. El mapeo a los puertos que consume <c>RateLimitQuotaResolver</c> vive en Program.cs,
    /// condicionado a <c>RateLimit:EnforceTierQuotas</c>.
    /// </summary>
    private static void AddRateLimitTierQuotas(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ITenantPlanCodeProjectionRepository, TenantPlanCodeProjectionRepository>();
        services.AddScoped<EfTenantPlanCodeReader>();
        services.AddScoped(sp => new CachedTenantPlanCodeReader(
            sp.GetRequiredService<BuildingBlocks.Caching.ICacheService>(),
            sp.GetRequiredService<EfTenantPlanCodeReader>()
        ));
        services.AddScoped<ITenantPlanCodeCacheInvalidator, TenantPlanCodeCacheInvalidator>();

        services
            .AddOptions<ServiceAuthClientOptions>()
            .Bind(configuration.GetSection(ServiceAuthClientOptions.SectionName));
        services.AddHttpClient<IServiceTokenAcquirer, ServiceTokenAcquirer>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<ServiceAuthClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.AuthBaseUrl));
                http.Timeout = TimeSpan.FromSeconds(15);
            }
        );

        services
            .AddOptions<SubscriptionClientOptions>()
            .Bind(configuration.GetSection(SubscriptionClientOptions.SectionName));
        services.AddHttpClient<HttpPlanRateLimitReader>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<SubscriptionClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.BaseUrl));
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );
    }

    /// <summary>
    /// Recuperación pull cuando <c>ProjectionPermissionsSource</c> encuentra un miss local. Reusa el
    /// <see cref="ServiceAuthClientOptions"/> y el <see cref="IServiceTokenAcquirer"/> ya registrados
    /// (apuntan a Auth): un HttpClient tipado más, no un segundo acquirer. Sin este registro los dos
    /// parámetros opcionales del constructor quedan null y el comportamiento es fail-closed puro.
    /// </summary>
    private static void AddPermissionsPullRecovery(IServiceCollection services)
    {
        services.AddScoped<IUserPermissionsProjectionWriter, PermissionsProjectionWriter>();
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

    /// <summary>
    /// <see cref="UserPermissionsProjectionRepository"/> implementa el puerto local y el de
    /// BuildingBlocks. Se registra la clase concreta para que las dos interfaces resuelvan la misma
    /// instancia scoped; si no, habría dos trackers distintos en el mismo request.
    /// </summary>
    private static void AddPermissionsProjections(IServiceCollection services)
    {
        services.AddScoped<UserPermissionsProjectionRepository>();
        services.AddScoped<IUserPermissionsProjectionRepository>(p =>
            p.GetRequiredService<UserPermissionsProjectionRepository>()
        );
        services.AddScoped<IUserPermissionsProjectionReader>(p =>
            p.GetRequiredService<UserPermissionsProjectionRepository>()
        );
        services.AddScoped<IRolePermissionsProjectionRepository, RolePermissionsProjectionRepository>();
    }
}
