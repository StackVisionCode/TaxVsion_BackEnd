using System.Linq.Expressions;
using System.Reflection;
using BuildingBlocks.Domain;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Domain.Analytics;
using TaxVision.Signature.Domain.Audit;
using TaxVision.Signature.Domain.Consents;
using TaxVision.Signature.Domain.Permissions;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Domain.RateLimiting;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Settings;
using TaxVision.Signature.Domain.Templates;
using TaxVision.Signature.Domain.Validation;

namespace TaxVision.Signature.Infrastructure.Persistence;

public sealed class SignatureDbContext(DbContextOptions<SignatureDbContext> options, ITenantContext tenantContext)
    : DbContext(options),
        IUnitOfWork
{
    public DbSet<TenantSignatureSettings> TenantSignatureSettings => Set<TenantSignatureSettings>();

    public DbSet<SignatureRequest> SignatureRequests => Set<SignatureRequest>();

    public DbSet<Signer> Signers => Set<Signer>();

    public DbSet<SignatureField> SignatureFields => Set<SignatureField>();

    public DbSet<SignerVerificationChallenge> SignerVerificationChallenges => Set<SignerVerificationChallenge>();

    public DbSet<CustomerEmailProjection> CustomerEmailProjections => Set<CustomerEmailProjection>();

    public DbSet<FileMetadataRef> FileMetadataRefs => Set<FileMetadataRef>();

    public DbSet<SignatureTemplate> SignatureTemplates => Set<SignatureTemplate>();

    public DbSet<TemplateSignerSlot> TemplateSignerSlots => Set<TemplateSignerSlot>();

    public DbSet<TemplateField> TemplateFields => Set<TemplateField>();

    public DbSet<SignatureAnalyticsSnapshot> SignatureAnalyticsSnapshots => Set<SignatureAnalyticsSnapshot>();

    public DbSet<DocumentValidationRecord> DocumentValidationRecords => Set<DocumentValidationRecord>();

    public DbSet<ConsentEvent> ConsentEvents => Set<ConsentEvent>();

    public DbSet<SignatureAuditEvent> SignatureAuditEvents => Set<SignatureAuditEvent>();

    public DbSet<SignerRoleAuditSnapshot> SignerRoleAuditSnapshots => Set<SignerRoleAuditSnapshot>();

    // RBAC Fase 7 — proyección de AUTORIZACIÓN (perm_v enforcement), distinta de
    // SignerRoleAuditSnapshot de arriba (esa es de auditoría, ver docblock de
    // AuthzUserPermissionsProjection).
    public DbSet<AuthzUserPermissionsProjection> AuthzUserPermissionsProjections =>
        Set<AuthzUserPermissionsProjection>();

    public DbSet<AuthzRolePermissionsProjection> AuthzRolePermissionsProjections =>
        Set<AuthzRolePermissionsProjection>();

    // RateLimit Fase 2 — proyección local de "¿qué PlanCode tiene este tenant hoy?", mantenida por
    // TenantPlanCodeProjectionConsumer (BuildingBlocks.Messaging.SubscriptionIntegrationEvents.
    // TenantEntitlementsChangedIntegrationEvent). Consultada por EfTenantPlanCodeReader.
    public DbSet<TenantPlanCodeProjection> TenantPlanCodeProjections => Set<TenantPlanCodeProjection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        ApplyGlobalTenantFilter(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }

    // Guid.Empty nunca es un tenant válido: sin tenant ambiental (scope de Wolverine, job de
    // background, tiempo de diseño) el filtro compara contra Empty → cero filas, en vez de abrir
    // acceso cross-tenant.
    //
    // Se lee vía `this` y no capturando `tenantContext` en la lambda: EF Core cachea el modelo
    // compilado POR TIPO de DbContext, así que un Expression.Constant sobre el servicio inyectado
    // queda congelado con el ITenantContext del PRIMER SignatureDbContext del proceso y todas las
    // requests siguientes leerían ese tenant viejo. Cerrar sobre `this` sí se reevalúa por instancia.
    private Guid TenantFilterId => tenantContext.HasTenant ? tenantContext.TenantId : Guid.Empty;

    /// <summary>
    /// H-10 — red de seguridad multi-tenant fail-CLOSED sobre las entidades <see cref="ITenantOwned"/>,
    /// igual que los otros 15 servicios. Antes era <c>!HasTenant || e.TenantId == CurrentTenantId</c>:
    /// sin tenant ambiental el filtro no aplicaba y una consulta que olvidara su <c>.Where</c>
    /// devolvía las filas de TODOS los tenants — justo en los scopes (consumers de Wolverine, jobs)
    /// donde el tenant ambiental está vacío y la red hace más falta.
    ///
    /// <para>
    /// El cambio se verificó como sin impacto de comportamiento antes de aplicarlo: de los 35 accesos
    /// a las 10 entidades <c>ITenantOwned</c>, 12 son escrituras (a las que un query filter no aplica)
    /// y las 23 lecturas ya usaban <c>IgnoreQueryFilters()</c> con un <c>TenantId ==</c> explícito. Ni
    /// una sola consulta dependía del comportamiento abierto.
    /// </para>
    /// </summary>
    private void ApplyGlobalTenantFilter(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantOwned).IsAssignableFrom(entity.ClrType))
                continue;

            var parameter = Expression.Parameter(entity.ClrType, "e");
            var tenantProperty = Expression.Property(parameter, nameof(ITenantOwned.TenantId));
            var tenantFilterIdProperty =
                typeof(SignatureDbContext).GetProperty(
                    nameof(TenantFilterId),
                    BindingFlags.Instance | BindingFlags.NonPublic
                ) ?? throw new InvalidOperationException("Tenant filter property was not found.");
            var tenantFilterId = Expression.Property(Expression.Constant(this), tenantFilterIdProperty);
            var lambda = Expression.Lambda(Expression.Equal(tenantProperty, tenantFilterId), parameter);

            modelBuilder.Entity(entity.ClrType).HasQueryFilter(lambda);
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
