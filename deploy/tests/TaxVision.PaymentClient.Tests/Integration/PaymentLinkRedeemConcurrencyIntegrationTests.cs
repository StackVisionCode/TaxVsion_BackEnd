using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentClient.Application.PaymentLinks.Commands.RedeemPaymentLink;
using TaxVision.PaymentClient.Domain.PaymentLinks;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.Tenants;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Infrastructure.Persistence;
using TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;
using TaxVision.PaymentClient.Tests.Application;

namespace TaxVision.PaymentClient.Tests.Integration;

/// <summary>
/// Contra el SQL Server real (user-secrets de PaymentClient o PAYMENTCLIENT_DB_CONNECTION): dos canjes
/// simultáneos del mismo enlace no pueden cobrar dos veces.
/// </summary>
public sealed class PaymentLinkRedeemConcurrencyIntegrationTests : IAsyncLifetime
{
    private static readonly DateTime NowUtc = DateTime.UtcNow;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _connectionString = PaymentClientIntegrationDatabase.ConnectionString;

    private PaymentLink _link = null!;

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }

    private PaymentClientDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<PaymentClientDbContext>().UseSqlServer(_connectionString).Options,
            new FixedTenant(_tenantId)
        );

    public async Task InitializeAsync()
    {
        await using var db = NewContext();
        db.Tenants.Add(
            Tenant
                .Register(
                    _tenantId,
                    "Concurrency Office",
                    $"cc-{_tenantId:N}"[..20],
                    TenantKind.Customer,
                    "Etc/UTC",
                    NowUtc
                )
                .Value
        );

        var stripe = TenantPaymentConfig
            .Create(
                _tenantId,
                PaymentProviderCode.Stripe,
                TenantPaymentMode.DirectApiKeys,
                "pk_test_office",
                StatementDescriptor.Create("ACME TAX SVC").Value,
                NowUtc
            )
            .Value;
        stripe.UpdateSecrets(
            EncryptedSecret.Create(new PrefixSecretProtector().Protect("sk_test_office")).Value,
            null,
            Guid.Empty,
            NowUtc
        );
        stripe.MarkActive(Guid.Empty, NowUtc);
        db.TenantPaymentConfigs.Add(stripe);

        _link = PaymentLink
            .Create(
                _tenantId,
                null,
                Money.Create(2500, "USD").Value,
                PaymentPurpose.Create(PaymentPurposeKind.InvoicePayment, "inv-concurrency").Value,
                PaymentLinkToken.Generate(),
                TimeSpan.FromDays(7),
                Guid.Empty,
                NowUtc
            )
            .Value;
        db.PaymentLinks.Add(_link);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.AuditEntries.Where(e => e.TenantId == _tenantId).ExecuteDeleteAsync();
        await db.TenantPayments.IgnoreQueryFilters().Where(p => p.TenantId == _tenantId).ExecuteDeleteAsync();
        await db.PaymentLinks.IgnoreQueryFilters().Where(l => l.TenantId == _tenantId).ExecuteDeleteAsync();
        await db.TenantPaymentConfigs.IgnoreQueryFilters().Where(c => c.TenantId == _tenantId).ExecuteDeleteAsync();
        await db.Tenants.Where(t => t.Id == _tenantId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task TwoContexts_AttachAndMarkUsed_SecondSaveFails()
    {
        await using var first = NewContext();
        await using var second = NewContext();
        var a = await first.PaymentLinks.SingleAsync(l => l.Id == _link.Id);
        var b = await second.PaymentLinks.SingleAsync(l => l.Id == _link.Id);
        var paymentA = Guid.NewGuid();

        Assert.True(a.AttachPaymentAttempt(paymentA, null, NowUtc).IsSuccess);
        Assert.True(a.MarkAsUsed(NowUtc).IsSuccess);
        Assert.True(b.AttachPaymentAttempt(Guid.NewGuid(), null, NowUtc).IsSuccess);
        Assert.True(b.MarkAsUsed(NowUtc).IsSuccess);

        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());

        await using var check = NewContext();
        var stored = await check.PaymentLinks.AsNoTracking().SingleAsync(l => l.Id == _link.Id);
        Assert.Equal(paymentA, stored.RelatedTenantPaymentId);
    }

    [Fact]
    public async Task TwoRedeemsInParallel_ChargeProviderOnce()
    {
        // El intento se confirma antes de cobrar: el perdedor choca ahí y nunca llega al proveedor.
        var provider = new IdempotentRecordingPaymentProvider(
            new(PaymentProviderCode.Stripe, "Stripe", "Publishable key", "Secret key", true, false)
        );

        var outcomes = await Task.WhenAll(RedeemCatchingAsync(provider), RedeemCatchingAsync(provider));

        await using var db = NewContext();
        var succeeded = await db
            .TenantPayments.IgnoreQueryFilters()
            .Where(p => p.TenantId == _tenantId && p.Status == PaymentStatus.Succeeded)
            .CountAsync();

        Assert.Equal(1, provider.DistinctChargedKeys);
        Assert.Equal(1, provider.Calls);
        Assert.True(succeeded <= 1, $"Expected at most one succeeded payment, found {succeeded}.");
        Assert.Contains(outcomes, ok => !ok);
    }

    /// <summary>true si el canje terminó con éxito; false si perdió la carrera (fallo o conflicto al guardar).</summary>
    private async Task<bool> RedeemCatchingAsync(IdempotentRecordingPaymentProvider provider)
    {
        await using var db = NewContext();
        try
        {
            var result = await TestChargeFlow.RedeemAsync(
                new RedeemPaymentLinkCommand(_link.Token.Value, PaymentProviderCode.Stripe, "card", ReceiptEmail: null),
                new PaymentLinkRepository(db),
                new TenantPaymentRepository(db),
                new TenantPaymentConfigRepository(db),
                InMemoryPaymentClient.FakeAdapters(provider),
                new PrefixSecretProtector(),
                new PaymentAuditLogWriter(db),
                db,
                InMemoryPaymentClient.RecordingBus([]),
                new NoOpPaymentClientMetrics(),
                new FixedCorrelationContext(),
                NullLogger<PaymentLink>.Instance,
                CancellationToken.None
            );
            return result.IsSuccess;
        }
        catch (Exception ex) when (ex is DbUpdateException or ConflictException)
        {
            return false;
        }
    }
}
