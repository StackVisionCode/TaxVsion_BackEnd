using System.Linq.Expressions;
using System.Reflection;
using BuildingBlocks.Domain;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Notification.Domain.Authorization;
using TaxVision.Notification.Domain.Emailing.Campaigns;
using TaxVision.Notification.Domain.Emailing.Configurations;
using TaxVision.Notification.Domain.Emailing.Layouts;
using TaxVision.Notification.Domain.Emailing.Sending;
using TaxVision.Notification.Domain.Emailing.Templates;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Permissions;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Infrastructure.Persistence;

/// <param name="tenantContext">
/// RBAC Fase 5 (RBAC_Hardening_Plan.md) — tenant del actor autenticado, poblado por
/// <c>JwtTenantContextMiddleware</c> desde el JWT. Alimenta el <c>HasQueryFilter</c> global
/// fail-closed (safety net EF Core). EmailProviderConfiguration/EmailTemplate/EmailTemplateVersion/
/// EmailLayout/EmailRecipient/EmailDeliveryLog/EmailCampaignRecipient deliberadamente NO
/// implementan <see cref="ITenantOwned"/> (System vs Tenant scope con TenantId nullable, o hijos
/// sin columna propia) — el filtro genérico no los alcanza, igual que en Scribe.
/// </param>
public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options, ITenantContext tenantContext)
    : DbContext(options),
        IUnitOfWork
{
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();
    public DbSet<NotificationDispatchAttempt> NotificationDispatchAttempts => Set<NotificationDispatchAttempt>();
    public DbSet<PushDeviceToken> PushDeviceTokens => Set<PushDeviceToken>();
    public DbSet<EmailProviderConfiguration> EmailProviderConfigurations => Set<EmailProviderConfiguration>();
    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();
    public DbSet<EmailTemplateVersion> EmailTemplateVersions => Set<EmailTemplateVersion>();
    public DbSet<EmailLayout> EmailLayouts => Set<EmailLayout>();
    public DbSet<OutboundEmailMessage> OutboundEmailMessages => Set<OutboundEmailMessage>();
    public DbSet<EmailRecipient> EmailRecipients => Set<EmailRecipient>();
    public DbSet<EmailDeliveryLog> EmailDeliveryLogs => Set<EmailDeliveryLog>();
    public DbSet<EmailCampaign> EmailCampaigns => Set<EmailCampaign>();
    public DbSet<EmailCampaignRecipient> EmailCampaignRecipients => Set<EmailCampaignRecipient>();
    public DbSet<UserPermissionsProjection> UserPermissionsProjections => Set<UserPermissionsProjection>();
    public DbSet<RolePermissionsProjection> RolePermissionsProjections => Set<RolePermissionsProjection>();
    public DbSet<UserNotificationPreference> UserNotificationPreferences => Set<UserNotificationPreference>();

    // RBAC Fase 7 — proyecciones locales de permisos para AUTORIZACIÓN (perm_v enforcement),
    // distintas de UserPermissionsProjection/RolePermissionsProjection de arriba (Fase 4,
    // fan-out de notificaciones). Ver TaxVision.Notification.Domain.Authorization.AuthzUserPermissionsProjection.
    public DbSet<AuthzUserPermissionsProjection> AuthzUserPermissionsProjections =>
        Set<AuthzUserPermissionsProjection>();
    public DbSet<AuthzRolePermissionsProjection> AuthzRolePermissionsProjections =>
        Set<AuthzRolePermissionsProjection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        ApplyFailClosedTenantFilter(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// RBAC Fase 5 — tenant efectivo para el filtro, expuesto como miembro de ESTA instancia de
    /// DbContext (no del servicio inyectado directo): EF Core cachea el modelo compilado por tipo
    /// de DbContext, así que cerrar la expresión del filtro sobre <c>tenantContext</c> (constante
    /// externa) la congelaría con el valor del primer contexto construido en el proceso. Cerrar
    /// sobre <c>this</c> sí se reevalúa por-instancia.
    /// </summary>
    private Guid EffectiveTenantId => tenantContext.HasTenant ? tenantContext.TenantId : Guid.Empty;

    /// <summary>
    /// Safety net EF Core (defense-in-depth): filtra toda entidad <see cref="ITenantOwned"/> por
    /// el tenant del actor autenticado. Fail-closed — sin tenant en contexto, compara contra
    /// <see cref="Guid.Empty"/> (0 filas). <c>CampaignSchedulerService</c> (cross-tenant por
    /// diseño) usa <c>IgnoreQueryFilters()</c> explícito — ver EmailCampaignRepository.GetDueAsync.
    /// </summary>
    private void ApplyFailClosedTenantFilter(ModelBuilder modelBuilder)
    {
        var contextConstant = Expression.Constant(this);
        var effectiveTenantIdAccess = Expression.Property(contextConstant, nameof(EffectiveTenantId));

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType))
                continue;

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var tenantProperty = Expression.Property(parameter, nameof(ITenantOwned.TenantId));

            var filter = Expression.Lambda(Expression.Equal(tenantProperty, effectiveTenantIdAccess), parameter);
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
        }
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new ConflictException(
                "Persistence.UniqueConstraint",
                "A record with the same unique values already exists.",
                ex
            );
        }
    }
}
