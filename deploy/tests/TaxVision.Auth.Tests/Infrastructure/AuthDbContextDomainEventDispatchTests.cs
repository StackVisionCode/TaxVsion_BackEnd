using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Domain.TenantDomains;
using TaxVision.Auth.Domain.TenantDomains.Events;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Infrastructure;

/// <summary>
/// Fase A7 — prueba de plomería: AuthDbContext.SaveChangesAsync drena y publica los
/// domain events de los agregados rastreados ANTES del commit. Usa el proveedor
/// InMemory de EF Core, no el mecanismo de ruteo de Wolverine (eso ya está probado
/// por Wolverine mismo) — solo el glue code nuevo en SaveChangesAsync.
/// </summary>
public sealed class AuthDbContextDomainEventDispatchTests
{
    /// <summary>RBAC Fase 5 — sin tenant seteado a propósito: estos tests solo agregan/guardan, nunca vuelven a leer.</summary>
    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    private static AuthDbContext CreateContext(FakeMessageBus bus) =>
        new(
            new DbContextOptionsBuilder<AuthDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            bus,
            new NoTenantContext()
        );

    [Fact]
    public async Task SaveChangesAsync_dispatches_pending_domain_events_and_clears_them()
    {
        var bus = new FakeMessageBus();
        await using var db = CreateContext(bus);

        var slug = SubdomainSlug.Create("oficina1").Value;
        var domain = TenantDomain
            .CreateSubdomain(Guid.NewGuid(), slug, "taxprocore.com", Guid.NewGuid(), DateTime.UtcNow)
            .Value;

        await db.TenantDomains.AddAsync(domain);
        await db.SaveChangesAsync();

        var published = Assert.Single(bus.Published.OfType<TenantDomainCreated>());
        Assert.Equal(domain.Id, published.DomainId);
        Assert.Empty(domain.DomainEvents);
    }

    [Fact]
    public async Task SaveChangesAsync_with_no_pending_events_publishes_nothing()
    {
        var bus = new FakeMessageBus();
        await using var db = CreateContext(bus);

        var slug = SubdomainSlug.Create("oficina2").Value;
        var domain = TenantDomain
            .CreateSubdomain(Guid.NewGuid(), slug, "taxprocore.com", Guid.NewGuid(), DateTime.UtcNow)
            .Value;
        await db.TenantDomains.AddAsync(domain);
        await db.SaveChangesAsync();
        bus.Published.Clear();

        domain.ClearDomainEvents(); // ya sin eventos pendientes tras el save anterior
        await db.SaveChangesAsync();

        Assert.Empty(bus.Published);
    }
}
