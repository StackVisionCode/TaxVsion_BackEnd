using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.ProcessTenantWebhook;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Domain.Webhooks;
using TaxVision.PaymentClient.Infrastructure.Persistence;
using TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;
using TaxVision.PaymentClient.Infrastructure.Providers;
using TaxVision.PaymentClient.Infrastructure.Providers.PayPal;
using TaxVision.PaymentClient.Tests.Infrastructure;

namespace TaxVision.PaymentClient.Tests.Application;

/// <summary>Un reembolso hecho en PayPal llega por webhook y queda en el pago: handler real, repositorios
/// en memoria y el adaptador PayPal real con HTTP falso (las claves y el Webhook ID salen del config).</summary>
public sealed class PayPalWebhookFlowTests
{
    private const string WebhookId = "8PT597110X687430LKGECATA";
    private static readonly DateTime NowUtc = DateTime.UtcNow;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly PaymentClientDbContext _db = InMemoryPaymentClient.CreateDb();

    private const string RefundEvent = """
        {"id":"WH-REFUND-1","resource_type":"refund","event_type":"PAYMENT.CAPTURE.REFUNDED",
         "resource":{"id":"REF-1","status":"COMPLETED","amount":{"value":"60.00","currency_code":"USD"},
                     "seller_payable_breakdown":{"total_refunded_amount":{"value":"60.00","currency_code":"USD"}},
                     "links":[{"href":"https://api-m.sandbox.paypal.com/v2/payments/captures/CAP-999","rel":"up"}]}}
        """;

    [Fact]
    public async Task A_verified_refund_is_applied_once_to_the_captured_payment()
    {
        var payment = await SeedPayPalOfficeWithCapturedPaymentAsync();
        var payPal = new PayPalStub(PayPalWebhookTests.VerificationResponse("SUCCESS"));

        var first = await DeliverAsync(payPal);
        var again = await DeliverAsync(new PayPalStub(PayPalWebhookTests.VerificationResponse("SUCCESS")));

        Assert.True(first.IsSuccess);
        Assert.True(again.IsSuccess);
        var stored = await _db.TenantPayments.IgnoreQueryFilters().Include(p => p.Refunds).SingleAsync();
        Assert.Equal(PaymentStatus.PartiallyRefunded, stored.Status);
        Assert.Equal(6000, Assert.Single(stored.Refunds).Amount.AmountCents);
        var webhookEvent = await _db.WebhookEvents.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(WebhookEventStatus.Applied, webhookEvent.Status);
        Assert.Equal(PaymentProviderCode.PayPal, webhookEvent.ProviderCode);
        Assert.Equal(payment.Id, webhookEvent.RelatedTenantPaymentId);

        // Verificó con las claves y el Webhook ID de la oficina, contra su URL (sandbox).
        var verify = Assert.Single(payPal.Requests, r => r.Path == "/v1/notifications/verify-webhook-signature");
        Assert.Contains(WebhookId, verify.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_PayPal_down_nothing_is_recorded_and_the_delivery_fails_so_PayPal_retries()
    {
        await SeedPayPalOfficeWithCapturedPaymentAsync();

        var result = await DeliverAsync(new PayPalStub(verifyResponse: null, throwOnVerify: true));

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentStatus.Succeeded, (await _db.TenantPayments.IgnoreQueryFilters().SingleAsync()).Status);
        Assert.Empty(await _db.WebhookEvents.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task An_invalid_signature_is_rejected_without_touching_the_payment()
    {
        await SeedPayPalOfficeWithCapturedPaymentAsync();

        var result = await DeliverAsync(new PayPalStub(PayPalWebhookTests.VerificationResponse("FAILURE")));

        Assert.True(result.IsFailure);
        Assert.Equal("PayPal.WebhookSignature.Invalid", result.Error.Code);
        Assert.Equal(PaymentStatus.Succeeded, (await _db.TenantPayments.IgnoreQueryFilters().SingleAsync()).Status);
        Assert.Empty(await _db.WebhookEvents.IgnoreQueryFilters().ToListAsync());
    }

    private Task<Result> DeliverAsync(PayPalStub payPal)
    {
        var adapter = new PayPalPaymentAdapter(
            new StubHttpClientFactory(payPal),
            NullLogger<PayPalPaymentAdapter>.Instance
        );
        var services = new ServiceCollection()
            .AddKeyedSingleton<IPaymentProvider>(PaymentProviderCode.PayPal, adapter)
            .BuildServiceProvider();

        return ProcessTenantWebhookHandler.Handle(
            new ProcessTenantWebhookCommand(
                _tenantId,
                PaymentProviderCode.PayPal,
                RefundEvent,
                PayPalWebhookTests.SignedHeaders()
            ),
            new TenantPaymentConfigRepository(_db),
            new KeyedPaymentAdapterFactory(services, [PaymentProviderCode.PayPal]),
            new PrefixSecretProtector(),
            new WebhookEventRepository(_db),
            new TenantPaymentRepository(_db),
            new PaymentLinkRepository(_db),
            new PaymentAuditLogWriter(_db),
            _db,
            InMemoryPaymentClient.RecordingBus([]),
            new NoOpPaymentClientMetrics(),
            new FixedCorrelationContext(),
            NullLogger<WebhookEvent>.Instance,
            CancellationToken.None
        );
    }

    private async Task<TenantPayment> SeedPayPalOfficeWithCapturedPaymentAsync()
    {
        var config = TenantPaymentConfig
            .Create(
                _tenantId,
                PaymentProviderCode.PayPal,
                TenantPaymentMode.DirectApiKeys,
                "paypal-client-id",
                StatementDescriptor.Create("ACME TAX SVC").Value,
                NowUtc,
                "https://api-m.sandbox.paypal.com"
            )
            .Value;
        config.UpdateSecrets(Encrypted("paypal-client-secret"), Encrypted(WebhookId), Guid.Empty, NowUtc);
        config.MarkActive(Guid.Empty, NowUtc);

        // Capturado: la referencia es el id de la captura, como la guarda AuthorizeChargeAsync.
        var payment = TenantPayment
            .Create(
                _tenantId,
                IdempotencyKey.Create("paypal-charge").Value,
                Money.Create(15000, "USD").Value,
                null,
                PaymentPurpose.Create(PaymentPurposeKind.InvoicePayment, "inv-1").Value,
                PaymentProviderCode.PayPal,
                StatementDescriptor.Create("ACME TAX SVC").Value,
                Guid.Empty,
                NowUtc
            )
            .Value;
        payment.MarkProcessing(
            ExternalPaymentReference.Create(PaymentProviderCode.PayPal, "CAP-999").Value,
            null,
            null,
            Guid.Empty,
            NowUtc
        );
        payment.MarkSucceeded(NowUtc, Guid.Empty);

        _db.TenantPaymentConfigs.Add(config);
        _db.TenantPayments.Add(payment);
        await _db.SaveChangesAsync();
        return payment;
    }

    private static EncryptedSecret Encrypted(string plaintext) =>
        EncryptedSecret.Create(new PrefixSecretProtector().Protect(plaintext)).Value;
}
