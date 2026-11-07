using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.Tenants;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Tests.Integration;

/// <summary>PCL-6: una referencia del proveedor no puede quedar en dos pagos del mismo tenant.</summary>
public sealed class TenantPaymentExternalChargeIndexIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _otherTenantId = Guid.NewGuid();
    private readonly string _reference = $"pi_pcl6_{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        await using var db = PaymentClientTestDatabase.NewContext(_tenantId);
        foreach (var tenant in new[] { _tenantId, _otherTenantId })
            db.Tenants.Add(
                Tenant
                    .Register(
                        tenant,
                        "Index Office",
                        $"ix-{tenant:N}"[..20],
                        TenantKind.Customer,
                        "Etc/UTC",
                        DateTime.UtcNow
                    )
                    .Value
            );
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = PaymentClientTestDatabase.NewContext(_tenantId);
        await db.Set<TenantPaymentAttempt>()
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == _tenantId || a.TenantId == _otherTenantId)
            .ExecuteDeleteAsync();
        await db
            .TenantPayments.IgnoreQueryFilters()
            .Where(p => p.TenantId == _tenantId || p.TenantId == _otherTenantId)
            .ExecuteDeleteAsync();
        await db
            .Tenants.IgnoreQueryFilters()
            .Where(t => t.Id == _tenantId || t.Id == _otherTenantId)
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task DuplicateExternalChargeReference_IsRejected()
    {
        await using var db = PaymentClientTestDatabase.NewContext(_tenantId);
        db.TenantPayments.Add(Processing(_tenantId, _reference));
        await db.SaveChangesAsync();

        db.TenantPayments.Add(Processing(_tenantId, _reference));
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => db.SaveChangesAsync());

        Assert.Equal("Persistence.UniqueConstraint", conflict.Code);
    }

    [Fact]
    public async Task SameReference_InAnotherTenant_IsAccepted()
    {
        await using var db = PaymentClientTestDatabase.NewContext(_tenantId);
        db.TenantPayments.Add(Processing(_tenantId, _reference));
        db.TenantPayments.Add(Processing(_otherTenantId, _reference));

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task PaymentsWithoutReference_DoNotCollide()
    {
        await using var db = PaymentClientTestDatabase.NewContext(_tenantId);
        db.TenantPayments.Add(Pending(_tenantId));
        db.TenantPayments.Add(Pending(_tenantId));

        await db.SaveChangesAsync();
    }

    private static TenantPayment Processing(Guid tenantId, string reference)
    {
        var payment = Pending(tenantId);
        payment.MarkProcessing(
            ExternalPaymentReference.Create(PaymentProviderCode.Stripe, reference).Value,
            "processing",
            null,
            Guid.Empty,
            DateTime.UtcNow
        );
        return payment;
    }

    private static TenantPayment Pending(Guid tenantId) =>
        TenantPayment
            .Create(
                tenantId,
                IdempotencyKey.Create($"pcl6-{Guid.NewGuid():N}").Value,
                Money.Create(4900, "USD").Value,
                null,
                PaymentPurpose.Create(PaymentPurposeKind.Other, "pcl6").Value,
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("ACME TAX SVC").Value,
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;
}
