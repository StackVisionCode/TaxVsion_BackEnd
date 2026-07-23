using System.Linq.Expressions;
using System.Reflection;
using BuildingBlocks.Domain;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Scribe.Domain.EventMappings;
using TaxVision.Scribe.Domain.Layouts;
using TaxVision.Scribe.Domain.Permissions;
using TaxVision.Scribe.Domain.Projections;
using TaxVision.Scribe.Domain.Templates;

namespace TaxVision.Scribe.Infrastructure.Persistence;

/// <summary>Contexto de Entity Framework Core responsable de la persistencia del dominio Scribe.</summary>
/// <param name="tenantContext">
/// RBAC Fase 5 (RBAC_Hardening_Plan.md) — tenant del actor autenticado, poblado por
/// <c>JwtTenantContextMiddleware</c> desde el JWT. Alimenta el <c>HasQueryFilter</c> global
/// fail-closed (safety net EF Core). <c>EmailTemplate</c>/<c>EmailLayout</c>/
/// <c>EventTemplateMapping</c> son System-or-Tenant scoped (<c>TenantId</c> nullable) — implementan
/// <see cref="INullableTenantOwned"/>, no <see cref="ITenantOwned"/>, así que su filtro deja pasar
/// las filas System-scope (TenantId null) para cualquier tenant y solo aísla las Tenant-scope.
/// <c>TenantLogoRef</c>/<c>TenantLogoMissingNotification</c> son siempre tenant-específicas
/// (implementan <see cref="ITenantOwned"/> normal), pero su ÚNICO acceso de lectura real hoy es el
/// pipeline de render M2M (<c>RenderController</c>, <c>ActorType.Service</c> — sin claim
/// <c>tenant_id</c>, <c>EffectiveTenantId</c> siempre <see cref="Guid.Empty"/> ahí), que ya recibe
/// el tenantId explícito como parámetro — por eso esos 2 repos usan <c>IgnoreQueryFilters()</c> en
/// sus métodos de lectura (ver comentarios ahí). <c>EmailTemplateVersion</c>/
/// <c>TemplateVariableDefinition</c>/<c>EmailLayoutVersion</c> (hijos sin columna TenantId propia) y
/// <c>SystemAssetRef</c> (singleton de plataforma sin concepto de tenant) no implementan ninguna de
/// las dos interfaces.
/// </param>
public sealed class ScribeDbContext(DbContextOptions<ScribeDbContext> options, ITenantContext tenantContext)
    : DbContext(options),
        IUnitOfWork
{
    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();
    public DbSet<EmailTemplateVersion> EmailTemplateVersions => Set<EmailTemplateVersion>();
    public DbSet<TemplateVariableDefinition> TemplateVariableDefinitions => Set<TemplateVariableDefinition>();
    public DbSet<EmailLayout> EmailLayouts => Set<EmailLayout>();
    public DbSet<EmailLayoutVersion> EmailLayoutVersions => Set<EmailLayoutVersion>();
    public DbSet<EventTemplateMapping> EventTemplateMappings => Set<EventTemplateMapping>();
    public DbSet<TenantLogoRef> TenantLogoRefs => Set<TenantLogoRef>();
    public DbSet<TenantLogoMissingNotification> TenantLogoMissingNotifications => Set<TenantLogoMissingNotification>();
    public DbSet<SystemAssetRef> SystemAssetRefs => Set<SystemAssetRef>();
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
    /// igualdad estricta contra el tenant del actor autenticado (fail-closed — sin tenant en
    /// contexto, compara contra <see cref="Guid.Empty"/>, 0 filas). Para <see cref="INullableTenantOwned"/>
    /// (System-or-Tenant scoped) el filtro es <c>TenantId == null || TenantId == EffectiveTenantId</c>:
    /// las filas System-scope quedan siempre visibles (son defaults de plataforma, no de un tenant),
    /// solo las Tenant-scope se aíslan.
    /// </summary>
    private void ApplyFailClosedTenantFilter(ModelBuilder modelBuilder)
    {
        var contextConstant = Expression.Constant(this);
        var effectiveTenantIdAccess = Expression.Property(contextConstant, nameof(EffectiveTenantId));

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType))
            {
                var parameter = Expression.Parameter(entityType.ClrType, "e");
                var tenantProperty = Expression.Property(parameter, nameof(ITenantOwned.TenantId));

                var filter = Expression.Lambda(Expression.Equal(tenantProperty, effectiveTenantIdAccess), parameter);
                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
            }
            else if (typeof(INullableTenantOwned).IsAssignableFrom(entityType.ClrType))
            {
                var parameter = Expression.Parameter(entityType.ClrType, "e");
                var tenantProperty = Expression.Property(parameter, nameof(INullableTenantOwned.TenantId));

                var isSystemScope = Expression.Equal(tenantProperty, Expression.Constant(null, typeof(Guid?)));
                var matchesTenant = Expression.Equal(
                    tenantProperty,
                    Expression.Convert(effectiveTenantIdAccess, typeof(Guid?))
                );

                var filter = Expression.Lambda(Expression.OrElse(isSystemScope, matchesTenant), parameter);
                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
            }
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
