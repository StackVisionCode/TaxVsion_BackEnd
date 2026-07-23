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

    public DbSet<UserPermissionsProjection> UserPermissionsProjections => Set<UserPermissionsProjection>();

    // RBAC Fase 7 — proyección de AUTORIZACIÓN (perm_v enforcement), distinta de
    // UserPermissionsProjection de arriba (esa es de auditoría, ver docblock de
    // AuthzUserPermissionsProjection).
    public DbSet<AuthzUserPermissionsProjection> AuthzUserPermissionsProjections =>
        Set<AuthzUserPermissionsProjection>();

    public DbSet<AuthzRolePermissionsProjection> AuthzRolePermissionsProjections =>
        Set<AuthzRolePermissionsProjection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        ApplyGlobalTenantFilter(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }

    // RBAC Fase 5 fix (RBAC_Hardening_Plan.md) — bug real descubierto al escribir el mismo
    // mecanismo para Auth: EF Core cachea el modelo compilado POR TIPO de DbContext, no por
    // instancia. El filtro original cerraba sobre `tenantContext` (el servicio inyectado vía
    // constructor) con Expression.Constant — eso queda CONGELADO con el valor del PRIMER
    // SignatureDbContext jamás construido en el proceso; toda request siguiente (con su propio
    // ITenantContext scoped-per-request) seguiría leyendo ESE tenant viejo para siempre. Cerrar
    // sobre `this` (la propia instancia de DbContext) sí se reevalúa por-instancia — EF Core
    // reconoce ese patrón como parámetro, no como constante congelada.
    private bool HasTenant => tenantContext.HasTenant;
    private Guid CurrentTenantId => tenantContext.TenantId;

    // ------------------------------------------------------------------
    // Multi-tenant safety net: HasQueryFilter en TODAS las entidades que heredan
    // de TenantEntity. El filtro explícito por TenantId sigue en cada repo/read
    // service — pero si algún día un dev olvida el .Where(t => t.TenantId == ...),
    // esta red evita el leak cross-tenant (defense-in-depth).
    //
    // Si el ITenantContext no tiene tenant (migraciones, background jobs sin
    // scope, escenarios admin), el filtro no aplica: comportamiento "todo abierto"
    // — porque en esos casos NO hay concepto de tenant y romper todas las queries
    // sería peor que devolver más de lo que debería.
    // ------------------------------------------------------------------
    private void ApplyGlobalTenantFilter(ModelBuilder modelBuilder)
    {
        var contextConstant = Expression.Constant(this);
        var currentTenantAccess = Expression.Property(contextConstant, nameof(CurrentTenantId));
        var hasTenantAccess = Expression.Property(contextConstant, nameof(HasTenant));
        var noTenantSet = Expression.Not(hasTenantAccess);

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantOwned).IsAssignableFrom(entity.ClrType))
                continue;

            var parameter = Expression.Parameter(entity.ClrType, "e");
            var tenantProperty = Expression.Property(parameter, nameof(ITenantOwned.TenantId));

            // !HasTenant || e.TenantId == CurrentTenantId
            var equalsCurrent = Expression.Equal(tenantProperty, currentTenantAccess);
            var body = Expression.OrElse(noTenantSet, equalsCurrent);
            var lambda = Expression.Lambda(body, parameter);

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
