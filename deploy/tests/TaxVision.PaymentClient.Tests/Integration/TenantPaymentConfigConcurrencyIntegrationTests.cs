using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.Tenants;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Tests.Integration;

/// <summary>PCL-7: dos ediciones simultáneas de la misma configuración. La segunda choca; no gana en silencio.</summary>
public sealed class TenantPaymentConfigConcurrencyIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await using var db = PaymentClientTestDatabase.NewContext(_tenantId);
        db.Tenants.Add(
            Tenant
                .Register(
                    _tenantId,
                    "Config Office",
                    $"cf-{_tenantId:N}"[..20],
                    TenantKind.Customer,
                    "Etc/UTC",
                    DateTime.UtcNow
                )
                .Value
        );
        db.TenantPaymentConfigs.Add(
            TenantPaymentConfig
                .Create(
                    _tenantId,
                    PaymentProviderCode.PayPal,
                    TenantPaymentMode.DirectApiKeys,
                    "client-id",
                    StatementDescriptor.Create("ACME TAX SVC").Value,
                    DateTime.UtcNow
                )
                .Value
        );
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = PaymentClientTestDatabase.NewContext(_tenantId);
        await db.TenantPaymentConfigs.IgnoreQueryFilters().Where(c => c.TenantId == _tenantId).ExecuteDeleteAsync();
        await db.Tenants.IgnoreQueryFilters().Where(t => t.Id == _tenantId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task TwoContextsEditSameConfig_SecondFails()
    {
        await using var first = PaymentClientTestDatabase.NewContext(_tenantId);
        await using var second = PaymentClientTestDatabase.NewContext(_tenantId);
        var firstConfig = await first.TenantPaymentConfigs.SingleAsync(c => c.TenantId == _tenantId);
        var secondConfig = await second.TenantPaymentConfigs.SingleAsync(c => c.TenantId == _tenantId);

        firstConfig.UpdateApiBaseUrl("https://first.test", Guid.NewGuid(), DateTime.UtcNow);
        secondConfig.UpdateApiBaseUrl("https://second.test", Guid.NewGuid(), DateTime.UtcNow);

        await first.SaveChangesAsync();
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());

        Assert.Equal("Persistence.ConcurrencyConflict", conflict.Code);
    }
}
