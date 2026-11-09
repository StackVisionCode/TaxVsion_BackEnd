using BuildingBlocks.Messaging;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentApp.Infrastructure.Persistence;
using TaxVision.PaymentApp.Infrastructure.Persistence.Repositories;

namespace TaxVision.PaymentApp.Tests.Infrastructure;

/// <summary>PAY-9: un tenant inválido es un dato permanente; va directo a la cola de errores, sin 4 reintentos.</summary>
public sealed class TenantRegistryTests
{
    [Fact]
    public async Task Invalid_tenant_data_throws_permanent_exception()
    {
        await using var db = new PaymentAppDbContext(
            new DbContextOptionsBuilder<PaymentAppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenantContext()
        );
        var registry = new TenantRegistry(db);

        var thrown = await Record.ExceptionAsync(() =>
            registry.UpsertCreatedAsync(
                Guid.NewGuid(),
                "Acme",
                subDomain: "",
                TenantKind.Customer,
                "Etc/UTC",
                DateTime.UtcNow
            )
        );

        Assert.IsType<ArgumentException>(thrown);
        Assert.Contains("Tenant.InvalidSubDomain", thrown.Message);
        Assert.Contains(typeof(ArgumentException), WolverineFailurePolicies.PermanentFailureTypes);
    }

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
