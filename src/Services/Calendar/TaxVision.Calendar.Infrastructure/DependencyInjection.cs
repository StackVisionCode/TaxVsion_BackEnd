using BuildingBlocks.Caching;
using BuildingBlocks.CustomerVisibility;
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
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Availability.Abstractions;
using TaxVision.Calendar.Application.Backfill.Abstractions;
using TaxVision.Calendar.Application.Booking.Abstractions;
using TaxVision.Calendar.Application.Customers.Abstractions;
using TaxVision.Calendar.Application.Feeds.Abstractions;
using TaxVision.Calendar.Application.Observability;
using TaxVision.Calendar.Application.Office.Abstractions;
using TaxVision.Calendar.Application.Permissions.Abstractions;
using TaxVision.Calendar.Application.Portal.Abstractions;
using TaxVision.Calendar.Application.Projections.Abstractions;
using TaxVision.Calendar.Application.PublicBookings.Abstractions;
using TaxVision.Calendar.Application.RateLimiting.Abstractions;
using TaxVision.Calendar.Application.Staff.Abstractions;
using TaxVision.Calendar.Application.Types.Abstractions;
using TaxVision.Calendar.Infrastructure.Customers;
using TaxVision.Calendar.Infrastructure.Feeds;
using TaxVision.Calendar.Infrastructure.Jobs;
using TaxVision.Calendar.Infrastructure.Observability;
using TaxVision.Calendar.Infrastructure.Permissions;
using TaxVision.Calendar.Infrastructure.Persistence;
using TaxVision.Calendar.Infrastructure.Persistence.Repositories;
using TaxVision.Calendar.Infrastructure.RateLimiting;
using TaxVision.Calendar.Infrastructure.Staff;

namespace TaxVision.Calendar.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCalendarInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString =
            configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        services.AddDbContext<CalendarDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CalendarDbContext>());
        services.AddScoped<IAppointmentRepository, AppointmentRepository>();
        services.AddScoped<IOrganizerScheduleLock, SqlOrganizerScheduleLock>();
        services
            .AddOptions<OrganizerScheduleLockOptions>()
            .Bind(configuration.GetSection(OrganizerScheduleLockOptions.SectionName));
        services
            .AddOptions<TaxVision.Calendar.Application.Appointments.Commands.AttendeeResponseConflictRetryOptions>()
            .Bind(
                configuration.GetSection(
                    TaxVision
                        .Calendar
                        .Application
                        .Appointments
                        .Commands
                        .AttendeeResponseConflictRetryOptions
                        .SectionName
                )
            )
            .Validate(o => o.MaxAttempts > 0, "Calendar:AttendeeResponseConflictRetry:MaxAttempts must be positive.")
            .ValidateOnStart();

        // P2 — visibilidad por-cliente (kit compartido BuildingBlocks.CustomerVisibility): store de la
        // proyección sobre CalendarDbContext + reconciliación (siembra desde Customer con el token M2M de la
        // PlatformTenant) + flag (default OFF hasta sembrar). El consumer se engancha en Program.cs.
        services.AddCustomerVisibilityProjection<CalendarDbContext>();
        services.AddCustomerVisibilityReconciliation<Reconciliation.CalendarPlatformTokenProvider>(configuration);
        services
            .AddOptions<CalendarVisibilityOptions>()
            .Bind(configuration.GetSection(CalendarVisibilityOptions.SectionName));
        services.AddScoped<ICalendarFeedTokenRepository, CalendarFeedTokenRepository>();
        services.AddScoped<IAppointmentRsvpLookup, AppointmentRsvpLookup>();
        services.AddScoped<ICalendarFeedCache, CalendarFeedCache>();
        services.AddScoped<IAppointmentTypeRepository, AppointmentTypeRepository>();
        services.AddScoped<IAvailabilityRepository, AvailabilityRepository>();
        services.AddScoped<IOfficeScheduleRepository, OfficeScheduleRepository>();
        services.AddScoped<IBookingPolicyRepository, BookingPolicyRepository>();
        services.AddScoped<IBookingPageRepository, BookingPageRepository>();
        services.AddScoped<IPublicBookingRepository, PublicBookingRepository>();
        services.AddScoped<TaxVision.Calendar.Application.PublicBookings.PublicBookingScheduler>();
        services.AddScoped<TaxVision.Calendar.Application.PublicBookings.PublicBookingNotices>();
        services.AddSingleton<IRequesterKeyHasher, PublicBookings.HmacRequesterKeyHasher>();
        services
            .AddOptions<TaxVision.Calendar.Application.PublicBookings.PublicBookingOptions>()
            .Bind(
                configuration.GetSection(TaxVision.Calendar.Application.PublicBookings.PublicBookingOptions.SectionName)
            )
            .Validate(o => o.HoldMinutes is >= 1 and <= 60, "Calendar:PublicBooking:HoldMinutes must be 1-60.")
            .Validate(
                o => o.MaxRequestsPerEmailPerHour is >= 1 and <= 20,
                "Calendar:PublicBooking:MaxRequestsPerEmailPerHour must be 1-20."
            )
            .Validate(
                o => o.MaxSlotRangeDays is >= 1 and <= 31,
                "Calendar:PublicBooking:MaxSlotRangeDays must be 1-31."
            )
            .Validate(
                o => o.UnverifiedRetentionHours is >= 1 and <= 720,
                "Calendar:PublicBooking:UnverifiedRetentionHours must be 1-720."
            )
            .Validate(
                o => o.ClosedRetentionMonths is >= 1 and <= 120,
                "Calendar:PublicBooking:ClosedRetentionMonths must be 1-120."
            )
            .ValidateOnStart();
        // Código y enlace de gestión de las reservas públicas: solo salen cifrados (clave compartida con
        // Notification). Sin Encryption:MasterKey el envío falla cerrado, nunca en claro.
        services.AddProtectedPayloadProtection(configuration);
        services.AddScoped<IPortalCalendarStore, PortalCalendarStore>();
        services.AddScoped<TaxVision.Calendar.Application.Appointments.Search.IAppointmentSearch, AppointmentSearch>();
        // La misma instancia sirve las dos interfaces: el repositorio para los consumers y el lector
        // para ProjectionPermissionsSource, que es quien autoriza sin llamar a Auth.
        services.AddScoped<UserPermissionsProjectionRepository>();
        services.AddScoped<IUserPermissionsProjectionRepository>(p =>
            p.GetRequiredService<UserPermissionsProjectionRepository>()
        );
        services.AddScoped<IUserPermissionsProjectionReader>(p =>
            p.GetRequiredService<UserPermissionsProjectionRepository>()
        );
        services.AddScoped<IRolePermissionsProjectionRepository, RolePermissionsProjectionRepository>();

        AddCustomerDirectory(services, configuration);
        AddRateLimitTierQuotas(services, configuration);
        AddPermissionsPullRecovery(services);

        return services;
    }

    /// <summary>
    /// Escalado de cuotas por tier. Lo de aca esta siempre activo: el consumer de Subscription
    /// mantiene la proyeccion al dia aunque el flag este apagado. El mapeo a los puertos que consume
    /// el resolver vive en Program.cs, condicionado a RateLimit:EnforceTierQuotas.
    /// </summary>
    private static void AddRateLimitTierQuotas(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ITenantPlanCodeProjectionRepository, TenantPlanCodeProjectionRepository>();
        services.AddScoped<EfTenantPlanCodeReader>();
        // Gate de módulo Fase 1 — lector de módulos (fuente en Program.cs).
        services.AddCachedTenantEntitlementModulesReader<TaxVision.Calendar.Infrastructure.RateLimiting.EfTenantEntitlementModulesReader>();
        services.AddScoped(sp => new CachedTenantPlanCodeReader(
            sp.GetRequiredService<ICacheService>(),
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
    /// Recuperacion pull cuando la fuente de permisos encuentra un miss local. Reusa el acquirer ya
    /// registrado: un HttpClient tipado mas, no un segundo acquirer. Sin esto el comportamiento es
    /// fail-closed puro.
    /// </summary>
    private static void AddPermissionsPullRecovery(IServiceCollection services)
    {
        services.AddScoped<IUserPermissionsProjectionWriter, PermissionsProjectionWriter>();
        AddStaffContacts(services);
        services.AddHttpClient<IPermissionsSnapshotClient, PermissionsSnapshotClient>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<ServiceAuthClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.AuthBaseUrl));
                http.Timeout = TimeSpan.FromSeconds(15);
            }
        );
    }

    /// <summary>Nombre y correo del preparador para el portal (Auth M2M, en caché).</summary>
    private static void AddStaffContacts(IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddHttpClient<IStaffContactClient, StaffContactClient>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<ServiceAuthClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.AuthBaseUrl));
                http.Timeout = TimeSpan.FromSeconds(10);
            }
        );
        services.AddHttpClient<ITenantHostResolver, TenantHostResolver>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<ServiceAuthClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.AuthBaseUrl));
                http.Timeout = TimeSpan.FromSeconds(10);
            }
        );
    }

    private static string NormalizeBaseUrl(string url) => url.EndsWith('/') ? url : url + "/";

    private static void AddCustomerDirectory(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICustomerDirectoryRepository, CustomerDirectoryRepository>();
        services.AddScoped<ITenantBackfillStateRepository, TenantBackfillStateRepository>();

        services.AddOptions<CustomerClientOptions>().Bind(configuration.GetSection(CustomerClientOptions.SectionName));
        services.AddHttpClient<ICalendarCustomerClient, CalendarCustomerClient>(
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
        services.AddOptions<StartingSoonOptions>().Bind(configuration.GetSection(StartingSoonOptions.SectionName));

        // Uno rellena nombres faltantes de filas que ya existen; el otro inserta las filas que nunca
        // llegaron. Son huecos distintos y ninguno cubre al otro.
        services.AddHostedService<ReminderScheduleJob>();
        services.AddHostedService<MeetingLinkReconciliationJob>();
        services.AddHostedService<StartingSoonJob>();
        services.AddHostedService<CalendarRetentionJob>();
        services.AddHostedService<PublicBookingHoldExpiryJob>();
        services.AddHostedService<PublicBookingReminderJob>();
        services.AddHostedService<PublicBookingRetentionJob>();

        // Singleton: el Meter es uno por proceso y crear uno por scope multiplica instrumentos.
        services.AddSingleton<ICalendarMetrics, CalendarMetrics>();
        services.AddHostedService<CustomerDirectoryReconciliationJob>();
        services.AddHostedService<TenantCustomerFullReconciliationJob>();
    }
}
