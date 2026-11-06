using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Domain.TenantDomains;
using TaxVision.Auth.Domain.TenantDomains.Events;
using TaxVision.Auth.Infrastructure.Persistence;

namespace TaxVision.Auth.Tests.Infrastructure;

/// <summary>
/// AUTH-BB1: AuthDbContext.SaveChangesAsync ya no despacha ni limpia los eventos de dominio. Los deja en el
/// agregado para que Wolverine los saque al confirmar (UseTaxVisionDomainEvents) y los guarde en la misma
/// transacción. Si los limpiara, Wolverine no vería nada y el aviso se perdería.
/// </summary>
public sealed class AuthDbContextDomainEventDispatchTests
{
    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    [Fact]
    public async Task SaveChangesAsync_leaves_pending_domain_events_for_wolverine()
    {
        await using var db = new AuthDbContext(
            new DbContextOptionsBuilder<AuthDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenantContext()
        );
        var domain = TenantDomain
            .CreateSubdomain(
                Guid.NewGuid(),
                SubdomainSlug.Create("oficina1").Value,
                "taxproffice.com",
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;

        await db.TenantDomains.AddAsync(domain);
        await db.SaveChangesAsync();

        var pending = Assert.Single(domain.DomainEvents.OfType<TenantDomainCreated>());
        Assert.Equal(domain.Id, pending.DomainId);
    }
}
