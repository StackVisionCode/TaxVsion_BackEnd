using BuildingBlocks.Messaging.PaymentClientIntegrationEvents;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.PaymentLinks.Commands.GetCheckoutPaymentStatus;
using TaxVision.PaymentClient.Domain.PaymentLinks;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.Tenants;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Infrastructure.Persistence;
using TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;

namespace TaxVision.PaymentClient.Tests.Application;

/// <summary>La página de pago, al volver del 3DS, pregunta el estado del intento actual del link: se
/// consulta al proveedor en el momento y solo se devuelve el estado.</summary>
public sealed class GetCheckoutPaymentStatusHandlerTests
{
    private const long AmountCents = 2500;
    private static readonly DateTime NowUtc = DateTime.UtcNow;

    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly PaymentClientDbContext _db;
    private readonly RecordingPaymentProvider _stripe = new(
        new(PaymentProviderCode.Stripe, "Stripe", "Publishable key", "Secret key", true, false)
    );
    private readonly List<object> _published = [];
    private readonly Guid _tenantId = Guid.NewGuid();

    public GetCheckoutPaymentStatusHandlerTests() => _db = InMemoryPaymentClient.CreateDb(_databaseName);

    [Fact]
    public async Task Status_AfterThreeDsCompleted_ReconcilesAndReturnsSucceeded()
    {
        var (link, payment) = await SeedRequiresActionAsync();
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

        var result = await StatusAsync(link.Token.Value, payment.Id);

        Assert.Equal(nameof(PaymentStatus.Succeeded), result.Value.Status);
        Assert.Single(_published.OfType<TenantPaymentSucceededIntegrationEvent>());
        await using var fresh = InMemoryPaymentClient.CreateDb(_databaseName);
        Assert.Equal(
            PaymentLinkStatus.Used,
            (await fresh.PaymentLinks.IgnoreQueryFilters().SingleAsync(l => l.Id == link.Id)).Status
        );
    }

    [Fact]
    public async Task Status_ForPaymentThatIsNotTheLinksCurrentAttempt_IsNotFound()
    {
        var (link, _) = await SeedRequiresActionAsync();

        var result = await StatusAsync(link.Token.Value, Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentLink.NotFound", result.Error.Code);
        Assert.Empty(_stripe.StatusLookups);
    }

    [Fact]
    public async Task Status_WithUnknownToken_IsNotFound()
    {
        var (_, payment) = await SeedRequiresActionAsync();

        var result = await StatusAsync("no-such-token", payment.Id);

        Assert.Equal("PaymentLink.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Status_WhenProviderLookupFails_ReturnsCurrentStatus()
    {
        var (link, payment) = await SeedRequiresActionAsync();
        _stripe.ChargeStatus = _ => Result.Failure<WebhookEventPayload>(new Error("Stripe.ChargeStatus.Failed", "boom"));

        var result = await StatusAsync(link.Token.Value, payment.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(nameof(PaymentStatus.RequiresAction), result.Value.Status);
        Assert.Empty(_published);
    }

    [Fact]
    public void Response_ExposesOnlyTheStatus()
    {
        // Endpoint público: nada del pagador ni del proveedor.
        var properties = typeof(CheckoutPaymentStatusResponse).GetProperties().Select(p => p.Name);

        Assert.Equal(new[] { "Status" }, properties);
    }

    private Task<Result<CheckoutPaymentStatusResponse>> StatusAsync(string token, Guid tenantPaymentId) =>
        GetCheckoutPaymentStatusHandler.Handle(
            new GetCheckoutPaymentStatusCommand(token, tenantPaymentId),
            new PaymentLinkRepository(_db),
            new TenantPaymentRepository(_db),
            new TenantPaymentConfigRepository(_db),
            new TenantConnectAccountRepository(_db),
            InMemoryPaymentClient.FakeAdapters(_stripe),
            new PrefixSecretProtector(),
            Options.Create(
                new PlatformStripeCredentials { PlatformSecretKey = "sk_platform", ConnectWebhookSecret = "unused" }
            ),
            new PaymentAuditLogWriter(_db),
            _db,
            InMemoryPaymentClient.RecordingBus(_published),
            new NoOpPaymentClientMetrics(),
            new FixedCorrelationContext(),
            NullLogger<TenantPayment>.Instance,
            CancellationToken.None
        );

    private async Task<(PaymentLink Link, TenantPayment Payment)> SeedRequiresActionAsync()
    {
        _db.Tenants.Add(
            Tenant.Register(_tenantId, "Acme Tax Office", "acme-tax", TenantKind.Customer, "Etc/UTC", NowUtc).Value
        );
        var config = TenantPaymentConfig
            .Create(
                _tenantId,
                PaymentProviderCode.Stripe,
                TenantPaymentMode.DirectApiKeys,
                "pk_test_office",
                StatementDescriptor.Create("ACME TAX SVC").Value,
                NowUtc
            )
            .Value;
        config.UpdateSecrets(
            EncryptedSecret.Create(new PrefixSecretProtector().Protect("sk_test_office")).Value,
            null,
            Guid.Empty,
            NowUtc
        );
        config.MarkActive(Guid.Empty, NowUtc);
        _db.TenantPaymentConfigs.Add(config);

        var link = PaymentLink
            .Create(
                _tenantId,
                null,
                Money.Create(AmountCents, "USD").Value,
                PaymentPurpose.Create(PaymentPurposeKind.InvoicePayment, "inv-1").Value,
                PaymentLinkToken.Generate(),
                TimeSpan.FromDays(7),
                Guid.Empty,
                NowUtc
            )
            .Value;
        var payment = TenantPayment
            .Create(
                _tenantId,
                IdempotencyKey.Create($"paymentlink-redeem:{link.Id:N}:attempt:1").Value,
                Money.Create(AmountCents, "USD").Value,
                null,
                PaymentPurpose.Create(PaymentPurposeKind.InvoicePayment, "inv-1").Value,
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("ACME TAX SVC").Value,
                Guid.Empty,
                NowUtc
            )
            .Value;
        payment.MarkProcessing(
            ExternalPaymentReference.Create(PaymentProviderCode.Stripe, "pi_3ds").Value,
            null,
            null,
            Guid.Empty,
            NowUtc
        );
        payment.MarkRequiresAction("use_stripe_sdk", string.Empty, Guid.Empty, NowUtc);
        link.AttachPaymentAttempt(payment.Id, null, NowUtc);
        _db.TenantPayments.Add(payment);
        _db.PaymentLinks.Add(link);
        await _db.SaveChangesAsync();
        return (link, payment);
    }
}
