using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Quartz;
using TaxVision.Reminder.Application.Permissions.Abstractions;
using TaxVision.Reminder.Application.RateLimiting.Abstractions;
using TaxVision.Reminder.Application.Reminders.Abstractions;
using TaxVision.Reminder.Infrastructure.Observability;
using TaxVision.Reminder.Infrastructure.Permissions;
using TaxVision.Reminder.Infrastructure.Persistence;
using TaxVision.Reminder.Infrastructure.Persistence.Repositories;
using TaxVision.Reminder.Infrastructure.RateLimiting;
using TaxVision.Reminder.Infrastructure.Scheduling;

namespace TaxVision.Reminder.Infrastructure;

/// <summary>
/// La Fase 5 suma el scheduler de Quartz.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddReminderInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString =
            configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        services.AddDbContext<ReminderDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<ReminderDbContext>());
        services.AddScoped<IReminderRepository, ReminderRepository>();

        // RBAC Fase 3 — el repositorio de la proyección de usuarios implementa DOS interfaces sobre
        // la misma tabla: el puerto local rico (consumers) y el puerto angosto de BuildingBlocks
        // (ProjectionPermissionsSource). Se registra la clase concreta y ambas interfaces resuelven
        // a esa misma instancia scoped — si se registraran por separado habría dos DbContext
        // trackers distintos dentro del mismo request.
        services.AddScoped<UserPermissionsProjectionRepository>();
        services.AddScoped<IUserPermissionsProjectionRepository>(p =>
            p.GetRequiredService<UserPermissionsProjectionRepository>()
        );
        services.AddScoped<IUserPermissionsProjectionReader>(p =>
            p.GetRequiredService<UserPermissionsProjectionRepository>()
        );
        services.AddScoped<IRolePermissionsProjectionRepository, RolePermissionsProjectionRepository>();

        // Fase 9 — un solo Meter para todo el proceso: crear uno por scope multiplicaría
        // instrumentos con el mismo nombre y el exportador los emitiría por separado.
        services.AddSingleton<IReminderMetrics, ReminderMetrics>();

        AddRateLimitTierQuotas(services, configuration);
        AddPermissionsPullRecovery(services);
        AddScheduling(services, configuration, connectionString);
        return services;
    }

    /// <summary>
    /// Quartz con <c>AdoJobStore</c> sobre la misma base del servicio (ADR-R-04). El wiring vive
    /// acá y no en Program.cs a propósito: Quartz es un detalle de infraestructura y la Api no debe
    /// conocer su API.
    ///
    /// <para>
    /// <b>Un solo scheduler compartido</b> para todos los tenants (ADR-R-05). El aislamiento es el
    /// <c>trigger group</c> = <c>tenant:{id}</c>, no un scheduler por tenant: con miles de tenants
    /// eso serían miles de thread pools.
    /// </para>
    /// </summary>
    private static void AddScheduling(
        IServiceCollection services,
        IConfiguration configuration,
        string connectionString
    )
    {
        services
            .AddOptions<ReminderSchedulingOptions>()
            .Bind(configuration.GetSection(ReminderSchedulingOptions.SectionName));

        services.AddQuartz(q =>
        {
            q.SchedulerName = "reminder-scheduler";

            // AUTO da un InstanceId distinto por réplica. Con clustering activado, dos réplicas con
            // el mismo id se roban los triggers entre sí y el scheduler entra en checkin loop.
            q.SchedulerId = "AUTO";

            // Cuánto retraso tolera Quartz antes de considerar un trigger "misfired". No es la
            // ventana de gracia del negocio: ésa la aplica ReminderFireJob contra MisfireGraceMinutes.
            q.MisfireThreshold = TimeSpan.FromSeconds(60);

            q.UsePersistentStore(store =>
            {
                store.UseSqlServer(sql =>
                {
                    sql.ConnectionString = connectionString;
                    sql.TablePrefix = "QRTZ_";
                });

                // El binario por defecto congela los tipos CLR dentro de la fila: renombrar un
                // namespace dejaría triggers imposibles de deserializar.
                store.UseSystemTextJsonSerializer();

                // Fuerza JobDataMap de solo strings. Es lo que hace que ese contrato sobreviva a
                // cualquier refactor de tipos.
                store.UseProperties = true;

                store.UseClustering(cluster =>
                {
                    cluster.CheckinInterval = TimeSpan.FromSeconds(20);
                    cluster.CheckinMisfireThreshold = TimeSpan.FromSeconds(60);
                });
            });

            // Job durable y sin trigger propio: los triggers los crea QuartzReminderScheduler, uno
            // por recordatorio. Sin StoreDurably, Quartz rechaza registrarlo.
            q.AddJob<ReminderFireJob>(job => job.WithIdentity(ReminderFireJob.Key).StoreDurably());
        });

        // WaitForJobsToComplete evita que un despliegue corte un disparo a medias y deje el
        // recordatorio sin marcar.
        services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        services.AddSingleton<IReminderScheduler, QuartzReminderScheduler>();
        services.AddHostedService<ReminderScheduleReconciliationJob>();

        // Fase 10 — purga diaria de terminales viejos. Con Reminder:RetentionMonths=0 no borra nada.
        services.AddHostedService<ReminderRetentionJob>();
    }

    /// <summary>
    /// Escalado de cuotas por tier. Lo que se registra acá siempre está activo: el consumer del
    /// evento de Subscription mantiene <c>TenantPlanCodeProjections</c> al día incluso con el flag
    /// apagado, y los lectores concretos existen. El mapeo a los puertos que
    /// <c>RateLimitQuotaResolver</c> realmente consume vive en Program.cs, condicional a
    /// <c>RateLimit:EnforceTierQuotas</c>.
    ///
    /// <para>
    /// El acquirer M2M tiene dos consumidores: <c>HttpPlanRateLimitReader</c> (catálogo de
    /// PlanRateLimits de Subscription) y <c>PermissionsSnapshotClient</c> (recuperación pull contra
    /// Auth). Uno solo por servicio; los HttpClient tipados son uno por destino.
    /// </para>
    /// </summary>
    private static void AddRateLimitTierQuotas(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ITenantPlanCodeProjectionRepository, TenantPlanCodeProjectionRepository>();
        services.AddScoped<EfTenantPlanCodeReader>();
        services.AddScoped<BuildingBlocks.Infrastructure.RateLimiting.CachedTenantPlanCodeReader>(
            sp => new BuildingBlocks.Infrastructure.RateLimiting.CachedTenantPlanCodeReader(
                sp.GetRequiredService<BuildingBlocks.Caching.ICacheService>(),
                sp.GetRequiredService<EfTenantPlanCodeReader>()
            )
        );
        services.AddScoped<
            BuildingBlocks.RateLimiting.ITenantPlanCodeCacheInvalidator,
            TenantPlanCodeCacheInvalidator
        >();

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
            .AddOptions<BuildingBlocks.Infrastructure.RateLimiting.SubscriptionClientOptions>()
            .Bind(
                configuration.GetSection(
                    BuildingBlocks.Infrastructure.RateLimiting.SubscriptionClientOptions.SectionName
                )
            );
        services.AddHttpClient<BuildingBlocks.Infrastructure.RateLimiting.HttpPlanRateLimitReader>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<
                    IOptions<BuildingBlocks.Infrastructure.RateLimiting.SubscriptionClientOptions>
                >().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.BaseUrl));
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );
    }

    /// <summary>
    /// Recuperación pull bajo demanda cuando <c>ProjectionPermissionsSource</c> encuentra un miss
    /// local. Reusa el <c>ServiceAuthClientOptions</c> y el <c>IServiceTokenAcquirer</c> que ya
    /// registró <see cref="AddRateLimitTierQuotas"/> (ya apunta a Auth) — un HttpClient tipado más,
    /// no un segundo acquirer. Sin este registro los dos parámetros opcionales del constructor de
    /// <c>ProjectionPermissionsSource</c> quedan null y el comportamiento es fail-closed puro.
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
}
