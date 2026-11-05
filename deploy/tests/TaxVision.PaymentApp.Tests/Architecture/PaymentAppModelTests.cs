using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using TaxVision.PaymentApp.Infrastructure.Persistence;

namespace TaxVision.PaymentApp.Tests.Architecture;

/// <summary>El modelo EF declara los índices que impiden dos pagos con la misma referencia del proveedor.</summary>
public sealed class PaymentAppModelTests
{
    [Theory]
    [InlineData(
        "UX_SaaSPayments_ExternalChargeProvider_Reference",
        "ExternalChargeProvider,ExternalChargeReference",
        "[ExternalChargeReference] IS NOT NULL"
    )]
    [InlineData(
        "UX_SaaSPayments_ProviderCheckoutSessionId",
        "ProviderCheckoutSessionId",
        "[ProviderCheckoutSessionId] IS NOT NULL"
    )]
    public void SaaSPayments_has_unique_filtered_indexes_on_external_reference_and_session(
        string name,
        string columns,
        string filter
    )
    {
        // Sin conectar: solo se construye el modelo.
        using var db = new PaymentAppDbContext(
            new DbContextOptionsBuilder<PaymentAppDbContext>()
                .UseSqlServer("Server=(local);Database=PaymentAppModelTests;Trusted_Connection=True")
                .Options,
            new NoTenant()
        );
        var table = db.GetService<IDesignTimeModel>().Model.GetRelationalModel().FindTable("SaaSPayments", null);

        var index = Assert.Single(table!.Indexes, i => i.Name == name);
        Assert.True(index.IsUnique);
        Assert.Equal(filter, index.Filter);
        Assert.Equal(columns, string.Join(",", index.Columns.Select(c => c.Name)));
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
