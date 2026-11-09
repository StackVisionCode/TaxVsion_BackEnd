using BuildingBlocks.Messaging.PaymentClientIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.TenantConnect.Commands.ProcessConnectWebhook;
using TaxVision.PaymentClient.Domain.Connect;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Domain.Webhooks;
using TaxVision.PaymentClient.Infrastructure.Persistence;
using TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;

namespace TaxVision.PaymentClient.Tests.Application;

/// <summary>PCL-7: Stripe no garantiza el orden de <c>account.updated</c>; uno viejo no pisa el estado de uno nuevo.
/// PCL-9: los cobros en la cuenta conectada también se confirman por este webhook.</summary>
public sealed partial class ProcessConnectWebhookHandlerTests
{
    private const string AccountId = "acct_test_order";
    private const long AmountCents = 2500;

    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly PaymentClientDbContext _db;
    private readonly StubConnectGateway _gateway = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly List<object> _emitted = [];
    private readonly List<object> _published = [];
    private readonly CountingMetrics _metrics = new();

    public ProcessConnectWebhookHandlerTests() => _db = InMemoryPaymentClient.CreateDb(_dbName);

    [Fact]
    public async Task OlderAccountUpdated_AfterNewer_IsIgnored()
    {
        await SeedAccountAsync();
        var newer = DateTime.UtcNow;
        var older = newer.AddMinutes(-5);

        await DeliverAsync("evt_newer", chargesEnabled: true, occurredAtUtc: newer);
        await DeliverAsync("evt_older", chargesEnabled: false, occurredAtUtc: older);

        await using var read = InMemoryPaymentClient.CreateDb(_dbName);
        var account = await read.TenantConnectAccounts.IgnoreQueryFilters().SingleAsync();
        Assert.True(account.CanCharge);
        Assert.Equal(ConnectAccountStatus.Enabled, account.Status);
        Assert.Equal(newer, account.LastProviderEventAtUtc);
        var ignored = await read.WebhookEvents.IgnoreQueryFilters().SingleAsync(e => e.ProviderEventId == "evt_older");
        Assert.Equal(WebhookEventStatus.Applied, ignored.Status);
    }

    [Fact]
    public async Task NewerAccountUpdated_IsApplied()
    {
        await SeedAccountAsync();
        var first = DateTime.UtcNow.AddMinutes(-5);

        await DeliverAsync("evt_first", chargesEnabled: true, occurredAtUtc: first);
        await DeliverAsync("evt_second", chargesEnabled: false, occurredAtUtc: first.AddMinutes(1));

        await using var read = InMemoryPaymentClient.CreateDb(_dbName);
        var account = await read.TenantConnectAccounts.IgnoreQueryFilters().SingleAsync();
        Assert.False(account.CanCharge);
        Assert.Equal(ConnectAccountStatus.Restricted, account.Status);
    }

    [Fact]
    public async Task ConnectPaymentIntentSucceeded_MarksPaymentSucceeded()
    {
        await SeedAccountAsync();
        var paymentId = await SeedConnectPaymentAsync("pi_connect");

        await DeliverChargeAsync("evt_paid", Paid("pi_connect"));

        await using var read = InMemoryPaymentClient.CreateDb(_dbName);
        var payment = await read.TenantPayments.IgnoreQueryFilters().SingleAsync(p => p.Id == paymentId);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        var paid = Assert.Single(_emitted.OfType<TenantPaymentSucceededIntegrationEvent>());
        Assert.Equal(paymentId, paid.TenantPaymentId);
        var received = await read.WebhookEvents.IgnoreQueryFilters().SingleAsync(e => e.ProviderEventId == "evt_paid");
        Assert.Equal(WebhookEventStatus.Applied, received.Status);
    }

    [Fact]
    public async Task ConnectPaymentIntentSucceeded_TwoDeliveries_EmitOneSignal()
    {
        await SeedAccountAsync();
        await SeedConnectPaymentAsync("pi_connect");

        await DeliverChargeAsync("evt_paid", Paid("pi_connect"));
        await DeliverChargeAsync("evt_paid", Paid("pi_connect"));
        await DeliverChargeAsync("evt_paid_again", Paid("pi_connect"));

        Assert.Single(_emitted.OfType<TenantPaymentSucceededIntegrationEvent>());
        await using var read = InMemoryPaymentClient.CreateDb(_dbName);
        var again = await read
            .WebhookEvents.IgnoreQueryFilters()
            .SingleAsync(e => e.ProviderEventId == "evt_paid_again");
        Assert.Equal(WebhookEventStatus.Stale, again.Status);
    }

    [Fact]
    public async Task ConnectPaymentIntentFailed_MarksPaymentFailed()
    {
        await SeedAccountAsync();
        var paymentId = await SeedConnectPaymentAsync("pi_connect");

        await DeliverChargeAsync(
            "evt_failed",
            new WebhookEventPayload("pi_connect", PaymentStatus.Failed, "card_declined", "Declined.", null)
        );

        await using var read = InMemoryPaymentClient.CreateDb(_dbName);
        var payment = await read.TenantPayments.IgnoreQueryFilters().SingleAsync(p => p.Id == paymentId);
        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal("card_declined", payment.FailureCode);
        Assert.Null(payment.NextRetryAtUtc);
        Assert.Empty(_emitted.OfType<TenantPaymentSucceededIntegrationEvent>());
    }

    [Fact]
    public async Task ConnectPaymentIntentSucceeded_OtherAmount_IsNotAppliedAndNeedsReview()
    {
        await SeedAccountAsync();
        var paymentId = await SeedConnectPaymentAsync("pi_connect");

        await DeliverChargeAsync("evt_short", Paid("pi_connect", AmountCents - 1));

        await using var read = InMemoryPaymentClient.CreateDb(_dbName);
        var payment = await read.TenantPayments.IgnoreQueryFilters().SingleAsync(p => p.Id == paymentId);
        Assert.Equal(PaymentStatus.RequiresAction, payment.Status);
        Assert.Empty(_emitted.OfType<TenantPaymentSucceededIntegrationEvent>());
        var received = await read.WebhookEvents.IgnoreQueryFilters().SingleAsync(e => e.ProviderEventId == "evt_short");
        Assert.Equal(WebhookEventStatus.NeedsReview, received.Status);
    }

    [Fact]
    public async Task ConnectPaymentIntentSucceeded_ReferenceOfAnotherOffice_IsRejected()
    {
        await SeedAccountAsync();
        var paymentId = await SeedConnectPaymentAsync("pi_other_office", Guid.NewGuid());

        await DeliverChargeAsync("evt_other", Paid("pi_other_office"));

        await using var read = InMemoryPaymentClient.CreateDb(_dbName);
        var payment = await read.TenantPayments.IgnoreQueryFilters().SingleAsync(p => p.Id == paymentId);
        Assert.Equal(PaymentStatus.RequiresAction, payment.Status);
        var received = await read.WebhookEvents.IgnoreQueryFilters().SingleAsync(e => e.ProviderEventId == "evt_other");
        Assert.Equal(WebhookEventStatus.Rejected, received.Status);
    }

    private static WebhookEventPayload Paid(string reference, long amountCents = AmountCents) =>
        new(reference, PaymentStatus.Succeeded, null, null, null, PaidAmountCents: amountCents, PaidCurrency: "usd");

    private async Task DeliverChargeAsync(string eventId, WebhookEventPayload update)
    {
        _gateway.Next = new ConnectWebhookEvent(
            eventId,
            "payment_intent.succeeded",
            AccountId,
            ChargesEnabled: null,
            PayoutsEnabled: null,
            RequirementsCurrentlyDue: null,
            PayoutReference: null,
            PayoutAmountCents: null,
            PayoutCurrency: null,
            PayoutFailureReason: null,
            ChargeUpdate: update
        );
        await InvokeAsync();
    }

    private async Task<Guid> SeedConnectPaymentAsync(string reference, Guid? tenantId = null)
    {
        var now = DateTime.UtcNow;
        var payment = TenantPayment
            .Create(
                tenantId ?? _tenantId,
                IdempotencyKey.Create($"connect-{Guid.NewGuid():N}").Value,
                Money.Create(AmountCents, "USD").Value,
                null,
                PaymentPurpose.Create(PaymentPurposeKind.InvoicePayment, "inv-connect").Value,
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("ACME TAX SVC").Value,
                Guid.Empty,
                now
            )
            .Value;
        var split = SplitPaymentBreakdown.Create(AmountCents - 100, 100, null).Value;
        Assert.True(payment.MarkProcessingViaConnect(reference, split, null, null, Guid.Empty, now).IsSuccess);
        Assert.True(payment.MarkRequiresAction("use_stripe_sdk", "", Guid.Empty, now).IsSuccess);
        _db.TenantPayments.Add(payment);
        await _db.SaveChangesAsync();
        return payment.Id;
    }

    private async Task SeedAccountAsync()
    {
        _db.TenantConnectAccounts.Add(
            TenantConnectAccount
                .Create(
                    _tenantId,
                    PaymentProviderCode.Stripe,
                    ConnectAccountType.Express,
                    StripeConnectAccountId.Create(AccountId).Value,
                    DateTime.UtcNow
                )
                .Value
        );
        await _db.SaveChangesAsync();
    }

    private async Task DeliverAsync(string eventId, bool chargesEnabled, DateTime occurredAtUtc)
    {
        _gateway.Next = new ConnectWebhookEvent(
            eventId,
            "account.updated",
            AccountId,
            ChargesEnabled: chargesEnabled,
            PayoutsEnabled: chargesEnabled,
            RequirementsCurrentlyDue: [],
            PayoutReference: null,
            PayoutAmountCents: null,
            PayoutCurrency: null,
            PayoutFailureReason: null,
            OccurredAtUtc: occurredAtUtc
        );

        await InvokeAsync();
    }

    private async Task InvokeAsync()
    {
        var result = await HandleAsync();
        Assert.True(result.IsSuccess);
    }

    private async Task<Result> HandleAsync(IUnitOfWork? unitOfWork = null)
    {
        await using var db = InMemoryPaymentClient.CreateDb(_dbName);
        var result = await ProcessConnectWebhookHandler.Handle(
            new ProcessConnectWebhookCommand("{}", "t=1,v1=x"),
            _gateway,
            Options.Create(new PlatformStripeCredentials { PlatformSecretKey = "sk", ConnectWebhookSecret = "whsec" }),
            new WebhookEventRepository(db),
            new TenantConnectAccountRepository(db),
            new TenantPaymentConfigRepository(db),
            new PayoutScheduleRepository(db),
            new TenantPaymentRepository(db),
            new PaymentLinkRepository(db),
            new PaymentAuditLogWriter(db),
            unitOfWork ?? db,
            InMemoryPaymentClient.RecordingBus(_published),
            _metrics,
            new FixedCorrelationContext(),
            NullLogger<WebhookEvent>.Instance,
            CancellationToken.None
        );
        _emitted.AddRange(await DomainEventReplay.EmittedAsync([], db));
        return result;
    }

    private sealed class StubConnectGateway : IStripeConnectGateway
    {
        public ConnectWebhookEvent Next { get; set; } = null!;

        public Error? Rejection { get; set; }

        public Task<Result<string>> CreateAccountAsync(
            ConnectAccountType type,
            string tenantEmail,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<Result<string>> CreateOnboardingLinkAsync(
            string stripeConnectAccountId,
            string refreshUrl,
            string returnUrl,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<Result<ConnectAccountStatusSnapshot>> GetAccountStatusAsync(
            string stripeConnectAccountId,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<Result<ConnectWebhookEvent>> VerifyAndParseConnectWebhookAsync(
            string rawPayload,
            string signatureHeader,
            string webhookSecret,
            CancellationToken ct
        ) => Task.FromResult(Rejection is null ? Result.Success(Next) : Result.Failure<ConnectWebhookEvent>(Rejection));
    }
}
