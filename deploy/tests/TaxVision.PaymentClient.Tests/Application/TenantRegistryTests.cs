using BuildingBlocks.Messaging;
using BuildingBlocks.Tenancy;
using TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;

namespace TaxVision.PaymentClient.Tests.Application;

/// <summary>PCL-13: un tenant inválido es un dato permanente; va directo a la cola de errores, sin 4 reintentos.</summary>
public sealed class TenantRegistryTests
{
    [Fact]
    public async Task RegisterAsync_InvalidTenant_ThrowsArgumentException()
    {
        await using var db = InMemoryPaymentClient.CreateDb();
        var registry = new TenantRegistry(db);

        var thrown = await Record.ExceptionAsync(() =>
            registry.UpsertCreatedAsync(
                Guid.NewGuid(),
                name: "",
                "acme",
                TenantKind.Customer,
                "Etc/UTC",
                DateTime.UtcNow
            )
        );

        Assert.IsType<ArgumentException>(thrown);
        Assert.Contains("Tenant.InvalidName", thrown.Message);
        Assert.Contains(typeof(ArgumentException), WolverineFailurePolicies.PermanentFailureTypes);
    }
}
