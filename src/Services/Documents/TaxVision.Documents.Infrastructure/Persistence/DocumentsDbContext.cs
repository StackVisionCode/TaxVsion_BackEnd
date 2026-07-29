using System.Linq.Expressions;
using System.Reflection;
using BuildingBlocks.Domain;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Documents.Domain.Branding;
using TaxVision.Documents.Domain.Generations;
using TaxVision.Documents.Domain.Permissions;
using Wolverine;

namespace TaxVision.Documents.Infrastructure.Persistence;

/// <summary>
/// DbContext del servicio Documents. Persiste el estado del aggregate ANTES de drenar sus domain
/// events al outbox durable de Wolverine (misma transacción ambiental).
///
/// Filtro global fail-closed por ITenantOwned: dentro de un scope de Wolverine (consumer/job) el
/// ITenantContext ambiental está vacío → el filtro colapsa a Guid.Empty → los repos que corren en
/// ese scope deben usar .IgnoreQueryFilters() con un tenantId EXPLÍCITO (ver la guía de
/// IgnoreQueryFilters/Wolverine). GetByFileIdAsync es cross-tenant deliberado (correlación de un
/// evento de CloudStorage) y valida el tenant contra la generación encontrada.
/// </summary>
public sealed class DocumentsDbContext(
    DbContextOptions<DocumentsDbContext> options,
    ITenantContext tenantContext,
    IMessageBus? messageBus = null
) : DbContext(options), IUnitOfWork
{
    private readonly ITenantContext _tenantContext = tenantContext;
    private readonly IMessageBus? _messageBus = messageBus;

    public DbSet<DocumentGeneration> DocumentGenerations => Set<DocumentGeneration>();
    public DbSet<DocumentBranding> DocumentBrandings => Set<DocumentBranding>();
    public DbSet<AuthzUserPermissionsProjection> AuthzUserPermissionsProjections =>
        Set<AuthzUserPermissionsProjection>();
    public DbSet<AuthzRolePermissionsProjection> AuthzRolePermissionsProjections =>
        Set<AuthzRolePermissionsProjection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        ApplyFailClosedTenantFilters(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Persistir estado ANTES de despachar domain events; recién después publicarlos + limpiarlos.
            // Wolverine corre este SaveChanges dentro de su transacción ambiental, así que los eventos
            // publicados acá se encolan en el outbox durable y se entregan de forma atómica al commitear.
            var affected = await base.SaveChangesAsync(cancellationToken);
            await DispatchDomainEventsAsync(cancellationToken);
            return affected;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConflictException(
                "Persistence.ConcurrencyConflict",
                "The record changed while the operation was in progress.",
                ex
            );
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

    private async Task DispatchDomainEventsAsync(CancellationToken ct)
    {
        if (_messageBus is null)
            return;

        var roots = ChangeTracker
            .Entries<AggregateRoot>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        foreach (var root in roots)
        {
            var events = root.DomainEvents.ToArray();
            root.ClearDomainEvents();
            foreach (var domainEvent in events)
                await _messageBus.PublishAsync(domainEvent);
        }
    }

    private void ApplyFailClosedTenantFilters(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantOwned).IsAssignableFrom(entity.ClrType))
                continue;

            var parameter = Expression.Parameter(entity.ClrType, "entity");
            var tenantProperty = Expression.Property(parameter, nameof(ITenantOwned.TenantId));
            var tenantFilterIdProperty =
                typeof(DocumentsDbContext).GetProperty(
                    nameof(TenantFilterId),
                    BindingFlags.Instance | BindingFlags.NonPublic
                ) ?? throw new InvalidOperationException("Tenant filter property was not found.");
            var tenantFilterId = Expression.Property(Expression.Constant(this), tenantFilterIdProperty);
            var filter = Expression.Lambda(Expression.Equal(tenantProperty, tenantFilterId), parameter);

            modelBuilder.Entity(entity.ClrType).HasQueryFilter(filter);
        }
    }

    // Guid.Empty nunca es un tenant válido: cuando no hay tenant ambiental (scope de Wolverine),
    // el filtro compara contra Empty → cero filas, en vez de abrir acceso cross-tenant.
    private Guid TenantFilterId => _tenantContext.HasTenant ? _tenantContext.TenantId : Guid.Empty;
}
