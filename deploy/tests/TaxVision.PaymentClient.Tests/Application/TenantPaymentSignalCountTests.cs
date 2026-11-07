using BuildingBlocks.Domain;
using BuildingBlocks.Messaging.PaymentClientIntegrationEvents;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.PaymentLinks.Commands.RedeemPaymentLink;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.ChargeTenantPayment;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.ProcessTenantWebhook;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.ReconcileTenantPayment;
using TaxVision.PaymentClient.Domain.PaymentLinks;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.Tenants;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Domain.Webhooks;
using TaxVision.PaymentClient.Infrastructure.Persistence;
using TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;

namespace TaxVision.PaymentClient.Tests.Application;

/// <summary>
/// PCL-BB1: un solo aviso "pagado" por pago, venga por el camino que venga. Cuenta lo publicado a mano más lo
/// devuelto por el handler del evento de dominio; si quedan los dos caminos, el aviso sale dos veces. Ningún
/// evento de dominio sale por el bus: viaja solo por la cola domain-events.
/// </summary>
public sealed class TenantPaymentSignalCountTests
{
    private const long AmountCents = 2500;
    private static readonly DateTime LongAgo = DateTime.UtcNow.AddHours(-2);

    private readonly PaymentClientDbContext _db = InMemoryPaymentClient.CreateDb();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly List<object> _published = [];
    private readonly RecordingPaymentProvider _stripe = new(
        new(PaymentProviderCode.Stripe, "Stripe", "Publishable key", "Secret key", true, false)
    );

    [Theory]
    [InlineData("Redeem")]
    [InlineData("Webhook")]
    [InlineData("Reconciliation")]
    [InlineData("DirectCharge")]
    [InlineData("ZeroAmountCharge")]
    public async Task EachSuccessPath_EmitsExactlyOneTenantPaymentSucceeded(string path)
    {
        await SeedOfficeAsync();

        var paymentId = path switch
        {
            "Redeem" => await RedeemAsync(),
            "Webhook" => await WebhookAsync(),
            "Reconciliation" => await ReconcileAsync(),
            "DirectCharge" => await ChargeAsync(AmountCents),
            "ZeroAmountCharge" => await ChargeAsync(0),
            _ => throw new ArgumentOutOfRangeException(nameof(path)),
        };

        Assert.DoesNotContain(_published, message => message is IDomainEvent);
        var emitted = await DomainEventReplay.EmittedAsync(_published, _db);
        var paid = Assert.Single(emitted.OfType<TenantPaymentSucceededIntegrationEvent>());
        Assert.Equal(paymentId, paid.TenantPaymentId);
        Assert.Equal(_tenantId, paid.TenantId);
        Assert.Equal("inv-signal", paid.ExternalReferenceId);
    }

    [Fact]
    public async Task DeclinedCharge_EmitsNoSignal()
    {
        await SeedOfficeAsync();
        _stripe.Status = PaymentStatus.Failed;

        await ChargeAsync(AmountCents);

        Assert.Empty(await DomainEventReplay.EmittedAsync(_published, _db));
    }

    private async Task<Guid> RedeemAsync()
    {
        var link = PaymentLink
            .Create(
                _tenantId,
                null,
                Money.Create(AmountCents, "USD").Value,
                Purpose(),
                PaymentLinkToken.Generate(),
                TimeSpan.FromDays(7),
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;
        _db.PaymentLinks.Add(link);
        await _db.SaveChangesAsync();

        var result = await TestChargeFlow.RedeemAsync(
            new RedeemPaymentLinkCommand(link.Token.Value, PaymentProviderCode.Stripe, "pm_card", ReceiptEmail: null),
            new PaymentLinkRepository(_db),
            new TenantPaymentRepository(_db),
            new TenantPaymentConfigRepository(_db),
            InMemoryPaymentClient.FakeAdapters(_stripe),
            new PrefixSecretProtector(),
            new PaymentAuditLogWriter(_db),
            _db,
            InMemoryPaymentClient.RecordingBus(_published),
            new NoOpPaymentClientMetrics(),
            new FixedCorrelationContext(),
            NullLogger<PaymentLink>.Instance,
            CancellationToken.None
        );
        Assert.Equal(nameof(PaymentStatus.Succeeded), result.Value.Status);
        return result.Value.TenantPaymentId;
    }

    private async Task<Guid> WebhookAsync()
    {
        var payment = await SeedProcessingPaymentAsync("pi_hook");
        _stripe.Webhook = (
            new WebhookVerificationResult("evt_signal", "payment_intent.succeeded", "{}"),
            new WebhookEventPayload("pi_hook", PaymentStatus.Succeeded, null, null, null)
        );

        var result = await ProcessTenantWebhookHandler.Handle(
            new ProcessTenantWebhookCommand(
                _tenantId,
                PaymentProviderCode.Stripe,
                "{}",
                new Dictionary<string, string> { ["Stripe-Signature"] = "sig" }
            ),
            new TenantPaymentConfigRepository(_db),
            InMemoryPaymentClient.FakeAdapters(_stripe),
            new PrefixSecretProtector(),
            new WebhookEventRepository(_db),
            new TenantPaymentRepository(_db),
            new PaymentLinkRepository(_db),
            new PaymentAuditLogWriter(_db),
            _db,
            InMemoryPaymentClient.RecordingBus(_published),
            new NoOpPaymentClientMetrics(),
            new FixedCorrelationContext(),
            NullLogger<WebhookEvent>.Instance,
            CancellationToken.None
        );
        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        return payment.Id;
    }

    private async Task<Guid> ReconcileAsync()
    {
        var payment = await SeedProcessingPaymentAsync("pi_stuck");
        _stripe.ChargeStatus = lookup =>
            Result.Success(
                new WebhookEventPayload(
                    lookup.ProviderChargeReference,
                    PaymentStatus.Succeeded,
                    null,
                    null,
                    null,
                    PaidAmountCents: AmountCents,
                    PaidCurrency: "usd"
                )
            );

        var result = await ReconcileTenantPaymentHandler.Handle(
            new ReconcileTenantPaymentCommand(_tenantId, payment.Id, AbandonRequiresActionAfter: null),
            new TenantPaymentRepository(_db),
            new TenantPaymentConfigRepository(_db),
            new TenantConnectAccountRepository(_db),
            InMemoryPaymentClient.FakeAdapters(_stripe),
            new PrefixSecretProtector(),
            Options.Create(
                new PlatformStripeCredentials { PlatformSecretKey = "unused", ConnectWebhookSecret = "unused" }
            ),
            new PaymentLinkRepository(_db),
            new PaymentAuditLogWriter(_db),
            _db,
            InMemoryPaymentClient.RecordingBus(_published),
            new NoOpPaymentClientMetrics(),
            new FixedCorrelationContext(),
            NullLogger<TenantPayment>.Instance,
            CancellationToken.None
        );
        Assert.Equal(TenantPaymentReconciliation.Resolved, result.Value);
        return payment.Id;
    }

    private async Task<Guid> ChargeAsync(long amountCents)
    {
        var result = await TestChargeFlow.ChargeAsync(
            new ChargeTenantPaymentCommand(
                _tenantId,
                PaymentProviderCode.Stripe,
                amountCents,
                "USD",
                TaxpayerId: null,
                PaymentPurposeKind.InvoicePayment,
                PurposeExternalReferenceId: "inv-signal",
                PaymentMethodReference: "pm_card",
                ReceiptEmail: null,
                IdempotencyKey: $"signal-{Guid.NewGuid():N}",
                ActorUserId: Guid.NewGuid()
            ),
            new TenantPaymentRepository(_db),
            new TenantPaymentConfigRepository(_db),
            new TenantConnectAccountRepository(_db),
            InMemoryPaymentClient.FakeAdapters(_stripe),
            new PrefixSecretProtector(),
            Options.Create(
                new PlatformStripeCredentials { PlatformSecretKey = "unused", ConnectWebhookSecret = "unused" }
            ),
            new PaymentAuditLogWriter(_db),
            _db,
            new NoOpPaymentClientMetrics(),
            new FixedCorrelationContext(),
            NullLogger<TenantPayment>.Instance,
            CancellationToken.None
        );
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private async Task<TenantPayment> SeedProcessingPaymentAsync(string chargeReference)
    {
        var payment = TenantPayment
            .Create(
                _tenantId,
                IdempotencyKey.Create($"seed-{Guid.NewGuid():N}").Value,
                Money.Create(AmountCents, "USD").Value,
                null,
                Purpose(),
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("ACME TAX SVC").Value,
                Guid.Empty,
                LongAgo
            )
            .Value;
        payment.MarkProcessing(
            ExternalPaymentReference.Create(PaymentProviderCode.Stripe, chargeReference).Value,
            null,
            null,
            Guid.Empty,
            LongAgo
        );
        _db.TenantPayments.Add(payment);
        await _db.SaveChangesAsync();
        return payment;
    }

    private async Task SeedOfficeAsync()
    {
        _db.Tenants.Add(
            Tenant.Register(_tenantId, "Acme Tax Office", "acme-tax", TenantKind.Customer, "Etc/UTC", LongAgo).Value
        );
        var config = TenantPaymentConfig
            .Create(
                _tenantId,
                PaymentProviderCode.Stripe,
                TenantPaymentMode.DirectApiKeys,
                "pk_test_office",
                StatementDescriptor.Create("ACME TAX SVC").Value,
                LongAgo
            )
            .Value;
        var protector = new PrefixSecretProtector();
        config.UpdateSecrets(
            EncryptedSecret.Create(protector.Protect("sk_test_office")).Value,
            EncryptedSecret.Create(protector.Protect("whsec_office")).Value,
            Guid.Empty,
            LongAgo
        );
        config.MarkActive(Guid.Empty, LongAgo);
        _db.TenantPaymentConfigs.Add(config);
        await _db.SaveChangesAsync();
    }

    private static PaymentPurpose Purpose() =>
        PaymentPurpose.Create(PaymentPurposeKind.InvoicePayment, "inv-signal").Value;
}
