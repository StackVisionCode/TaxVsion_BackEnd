using System.Linq.Expressions;
using System.Reflection;
using BuildingBlocks.Domain;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Domain.Audit;
using TaxVision.Correspondence.Domain.Backfill;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.Permissions;
using TaxVision.Correspondence.Domain.Projections;

namespace TaxVision.Correspondence.Infrastructure.Persistence;

/// <summary>
/// Fase 2 agregó el primer DbSet real (CustomerEmailAddresses, proyección de emails de
/// Customer) más TenantBackfillStates (marca de backfill ya corrido, ver
/// TenantCustomerBackfillService). Fase 3 agrega el modelo de inbox (IncomingEmails +
/// EmailThreads, con sus child tables de recipients/attachments). Fase 4 agrega
/// UnmatchedIncomingEmails (cuarentena/debug del consumer de ingestion). Fase 10 agrega el
/// modelo de compose (Drafts + DraftRecipients). Fase 14 agrega CorrespondenceAuditLogs (rastro
/// mínimo de auditoría, primer uso real desde que el plan lo referencia en la §23).
/// </summary>
/// <param name="tenantContext">
/// RBAC Fase 5 (RBAC_Hardening_Plan.md) — tenant del actor autenticado, poblado por
/// <c>JwtTenantContextMiddleware</c> desde el JWT. Alimenta el <c>HasQueryFilter</c> global
/// fail-closed (safety net EF Core). A diferencia de los otros 11 servicios de esta fase, acá
/// NINGUNA de las 7 entidades raíz era <see cref="ITenantOwned"/> antes — todas llevaban
/// <c>TenantId</c> como campo plano. Retrofit hecho en esta fase: <c>Draft</c>/<c>EmailThread</c>/
/// <c>TenantBackfillState</c>/<c>IncomingEmail</c>/<c>CustomerEmailAddress</c>/
/// <c>UnmatchedIncomingEmail</c>/<c>CorrespondenceAuditLog</c> ahora implementan
/// <see cref="ITenantOwned"/>. <c>IncomingEmailRecipient</c>/<c>IncomingEmailAttachment</c>/
/// <c>DraftRecipient</c> (hijos sin columna TenantId propia, siempre cargados vía Include desde su
/// padre) deliberadamente NO la implementan — el filtro del padre ya los protege.
/// </param>
public sealed class CorrespondenceDbContext(
    DbContextOptions<CorrespondenceDbContext> options,
    ITenantContext tenantContext
) : DbContext(options), IUnitOfWork
{
    public DbSet<CustomerEmailAddress> CustomerEmailAddresses => Set<CustomerEmailAddress>();
    public DbSet<TenantBackfillState> TenantBackfillStates => Set<TenantBackfillState>();
    public DbSet<IncomingEmail> IncomingEmails => Set<IncomingEmail>();
    public DbSet<IncomingEmailRecipient> IncomingEmailRecipients => Set<IncomingEmailRecipient>();
    public DbSet<IncomingEmailAttachment> IncomingEmailAttachments => Set<IncomingEmailAttachment>();
    public DbSet<EmailThread> EmailThreads => Set<EmailThread>();
    public DbSet<UnmatchedIncomingEmail> UnmatchedIncomingEmails => Set<UnmatchedIncomingEmail>();
    public DbSet<Draft> Drafts => Set<Draft>();
    public DbSet<DraftRecipient> DraftRecipients => Set<DraftRecipient>();
    public DbSet<CorrespondenceAuditLog> CorrespondenceAuditLogs => Set<CorrespondenceAuditLog>();
    public DbSet<UserPermissionsProjection> UserPermissionsProjections => Set<UserPermissionsProjection>();
    public DbSet<RolePermissionsProjection> RolePermissionsProjections => Set<RolePermissionsProjection>();

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
    /// <see cref="Guid.Empty"/> (0 filas). <c>DraftRepository.ListAbandonedAsync</c>
    /// (DraftCleanupJob) y <c>TenantBackfillStateRepository.ListAllTenantIdsAsync</c>
    /// (CustomerEmailReconciliationJob) son los únicos 2 accesos cross-tenant genuinos — usan
    /// <c>IgnoreQueryFilters()</c> explícito. Ningún job de este servicio despacha vía
    /// <c>bus.InvokeAsync</c>, así que no hace falta sellar <c>bus.TenantId</c> en ninguno.
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
