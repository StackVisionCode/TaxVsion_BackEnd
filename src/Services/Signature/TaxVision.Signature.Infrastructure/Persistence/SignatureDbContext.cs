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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        ApplyGlobalTenantFilter(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }

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
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantOwned).IsAssignableFrom(entity.ClrType))
                continue;

            var parameter = Expression.Parameter(entity.ClrType, "e");
            var tenantProperty = Expression.Property(parameter, nameof(ITenantOwned.TenantId));

            // e.TenantId == tenantContext.TenantId
            var currentTenantAccess = Expression.Property(
                Expression.Constant(tenantContext),
                nameof(ITenantContext.TenantId)
            );
            var equalsCurrent = Expression.Equal(tenantProperty, currentTenantAccess);

            // !tenantContext.HasTenant
            var hasTenantAccess = Expression.Property(
                Expression.Constant(tenantContext),
                nameof(ITenantContext.HasTenant)
            );
            var noTenantSet = Expression.Not(hasTenantAccess);

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
