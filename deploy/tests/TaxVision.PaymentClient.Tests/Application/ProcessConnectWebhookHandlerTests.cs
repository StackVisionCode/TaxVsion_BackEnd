using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.TenantConnect.Commands.ProcessConnectWebhook;
using TaxVision.PaymentClient.Domain.Connect;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Domain.Webhooks;
using TaxVision.PaymentClient.Infrastructure.Persistence;
using TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;

namespace TaxVision.PaymentClient.Tests.Application;

/// <summary>PCL-7: Stripe no garantiza el orden de <c>account.updated</c>; uno viejo no pisa el estado de uno nuevo.</summary>
public sealed class ProcessConnectWebhookHandlerTests
{
    private const string AccountId = "acct_test_order";

    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly PaymentClientDbContext _db;
    private readonly StubConnectGateway _gateway = new();
    private readonly Guid _tenantId = Guid.NewGuid();

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

        await using var db = InMemoryPaymentClient.CreateDb(_dbName);
        var result = await ProcessConnectWebhookHandler.Handle(
            new ProcessConnectWebhookCommand("{}", "t=1,v1=x"),
            _gateway,
            Options.Create(new PlatformStripeCredentials { PlatformSecretKey = "sk", ConnectWebhookSecret = "whsec" }),
            new WebhookEventRepository(db),
            new TenantConnectAccountRepository(db),
            new TenantPaymentConfigRepository(db),
            new PayoutScheduleRepository(db),
            new PaymentAuditLogWriter(db),
            db,
            InMemoryPaymentClient.RecordingBus([]),
            new NoOpPaymentClientMetrics(),
            new FixedCorrelationContext(),
            NullLogger<WebhookEvent>.Instance,
            CancellationToken.None
        );
        Assert.True(result.IsSuccess);
    }

    private sealed class StubConnectGateway : IStripeConnectGateway
    {
        public ConnectWebhookEvent Next { get; set; } = null!;

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
        ) => Task.FromResult(Result.Success(Next));
    }
}
