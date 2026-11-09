using BuildingBlocks.Messaging.PaymentClientIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Domain.Audit;
using TaxVision.PaymentClient.Domain.Connect;
using TaxVision.PaymentClient.Domain.Payouts;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Domain.Webhooks;

namespace TaxVision.PaymentClient.Tests.Application;

/// <summary>CaracterizaciÃ³n de PCL-11: lo que hace cada tipo de evento antes de partir el handler.</summary>
public sealed partial class ProcessConnectWebhookHandlerTests
{
    [Fact]
    public async Task AccountEnabled_PublishesEnabled_ActivatesConfig_AndCountsOnboardingOnce()
    {
        await SeedAccountAsync();
        await SeedConnectConfigAsync();

        await DeliverAccountAsync("evt_1", chargesEnabled: true, []);
        await DeliverAccountAsync("evt_2", chargesEnabled: true, []);

        await using var read = InMemoryPaymentClient.CreateDb(_dbName);
        var config = await read.TenantPaymentConfigs.IgnoreQueryFilters().SingleAsync();
        var account = await read.TenantConnectAccounts.IgnoreQueryFilters().SingleAsync();
        Assert.True(config.IsActive);
        Assert.Equal(1, _metrics.OnboardingCompleted);
        Assert.Equal(2, _published.OfType<TenantConnectAccountEnabledIntegrationEvent>().Count());
        var enabled = _published.OfType<TenantConnectAccountEnabledIntegrationEvent>().First();
        Assert.Equal(_tenantId, enabled.TenantId);
        Assert.Equal(account.Id, enabled.TenantConnectAccountId);
        Assert.Equal("test-correlation-id", enabled.CorrelationId);
        var audit = await read.AuditEntries.IgnoreQueryFilters().ToListAsync();
        Assert.Equal(2, audit.Count);
        Assert.All(audit, entry => Assert.Equal(PaymentAuditAction.TenantConnectAccountUpdated, entry.Action));
    }

    [Fact]
    public async Task AccountRestricted_PublishesRestricted_WithItsRequirements()
    {
        await SeedAccountAsync();
        await DeliverAccountAsync("evt_1", chargesEnabled: true, []);
        _published.Clear();

        await DeliverAccountAsync("evt_2", chargesEnabled: false, ["external_account"]);

        var restricted = Assert.IsType<TenantConnectAccountRestrictedIntegrationEvent>(Assert.Single(_published));
        Assert.Equal(["external_account"], restricted.RequirementsCurrentlyDue);
    }

    [Fact]
    public async Task AccountPendingWithRequirements_PublishesOnboardingRequired()
    {
        await SeedAccountAsync();

        await DeliverAccountAsync("evt_1", chargesEnabled: false, ["tos_acceptance"]);

        var required = Assert.IsType<TenantConnectAccountOnboardingRequiredIntegrationEvent>(Assert.Single(_published));
        Assert.Equal(["tos_acceptance"], required.RequirementsCurrentlyDue);
        Assert.Equal(WebhookEventStatus.Applied, await StatusOfAsync("evt_1"));
    }

    [Fact]
    public async Task AccountPendingWithoutRequirements_PublishesNothing()
    {
        await SeedAccountAsync();

        await DeliverAccountAsync("evt_1", chargesEnabled: false, []);

        Assert.Empty(_published);
        Assert.Equal(WebhookEventStatus.Applied, await StatusOfAsync("evt_1"));
    }

    [Fact]
    public async Task OlderAccountEvent_PublishesNothing_AndIsNotAudited()
    {
        await SeedAccountAsync();
        var newer = DateTime.UtcNow;
        await DeliverAccountAsync("evt_new", chargesEnabled: true, [], newer);
        _published.Clear();

        await DeliverAccountAsync("evt_old", chargesEnabled: false, ["tos_acceptance"], newer.AddMinutes(-5));

        Assert.Empty(_published);
        await using var read = InMemoryPaymentClient.CreateDb(_dbName);
        Assert.Single(await read.AuditEntries.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task PayoutPaid_RecordsItAndPublishesCompleted()
    {
        await SeedAccountAsync();
        var schedule = await SeedScheduleAsync();

        await DeliverPayoutAsync("evt_p", "payout.paid", "po_1", 12_345, "usd", null);

        var completed = Assert.IsType<PayoutCompletedIntegrationEvent>(Assert.Single(_published));
        Assert.Equal(_tenantId, completed.TenantId);
        Assert.Equal(schedule, completed.PayoutScheduleId);
        Assert.Equal("po_1", completed.ProviderPayoutReference);
        Assert.Equal(12_345, completed.AmountCents);
        Assert.Equal("USD", completed.Currency);
        Assert.Equal("test-correlation-id", completed.CorrelationId);
        Assert.Equal(WebhookEventStatus.Applied, await StatusOfAsync("evt_p"));
        await using var read = InMemoryPaymentClient.CreateDb(_dbName);
        var stored = await read.PayoutSchedules.IgnoreQueryFilters().Include(s => s.Items).SingleAsync();
        var item = Assert.Single(stored.Items);
        Assert.Equal("po_1", item.ProviderPayoutReference);
        Assert.Equal(12_345, item.Amount.AmountCents);
        var audit = Assert.Single(await read.AuditEntries.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(PaymentAuditAction.PayoutScheduleUpdated, audit.Action);
    }

    [Fact]
    public async Task PayoutFailed_RecordsItAndPublishesFailed_WithItsReason()
    {
        await SeedAccountAsync();
        await SeedScheduleAsync();

        await DeliverPayoutAsync("evt_p", "payout.failed", "po_2", 500, "usd", "Account closed");

        var failed = Assert.IsType<PayoutFailedIntegrationEvent>(Assert.Single(_published));
        Assert.Equal("po_2", failed.ProviderPayoutReference);
        Assert.Equal(500, failed.AmountCents);
        Assert.Equal("Account closed", failed.FailureReason);
    }

    [Fact]
    public async Task PayoutFailed_WithoutReason_UsesTheDefaultReason()
    {
        await SeedAccountAsync();
        await SeedScheduleAsync();

        await DeliverPayoutAsync("evt_p", "payout.failed", "po_3", 500, "usd", null);

        Assert.Equal(
            "Payout failed.",
            Assert.IsType<PayoutFailedIntegrationEvent>(Assert.Single(_published)).FailureReason
        );
    }

    [Fact]
    public async Task Payout_WithoutCurrency_UsesTheScheduleCurrency()
    {
        await SeedAccountAsync();
        await SeedScheduleAsync();

        await DeliverPayoutAsync("evt_p", "payout.paid", "po_4", 700, null, null);

        Assert.Equal("USD", Assert.IsType<PayoutCompletedIntegrationEvent>(Assert.Single(_published)).Currency);
    }

    [Fact]
    public async Task Payout_WithoutSchedule_IsRejected()
    {
        await SeedAccountAsync();

        await DeliverPayoutAsync("evt_p", "payout.paid", "po_5", 700, "usd", null);

        Assert.Empty(_published);
        Assert.Equal(WebhookEventStatus.Rejected, await StatusOfAsync("evt_p"));
    }

    [Fact]
    public async Task Payout_WithInvalidCurrency_IsRejected_WithoutRecordingIt()
    {
        await SeedAccountAsync();
        await SeedScheduleAsync();

        await DeliverPayoutAsync("evt_p", "payout.paid", "po_6", 700, "dollars", null);

        Assert.Empty(_published);
        Assert.Equal(WebhookEventStatus.Rejected, await StatusOfAsync("evt_p"));
        await using var read = InMemoryPaymentClient.CreateDb(_dbName);
        Assert.Empty((await read.PayoutSchedules.IgnoreQueryFilters().Include(s => s.Items).SingleAsync()).Items);
    }

    [Fact]
    public async Task UnsupportedEvent_IsRejectedWithASuccess()
    {
        await SeedAccountAsync();

        await DeliverPayoutAsync("evt_x", "balance.available", null, null, null, null);

        Assert.Empty(_published);
        Assert.Equal(WebhookEventStatus.Rejected, await StatusOfAsync("evt_x"));
    }

    [Fact]
    public async Task UnknownAccount_IsASuccess_AndStoresNothing()
    {
        await DeliverAccountAsync("evt_u", chargesEnabled: true, []);

        await using var read = InMemoryPaymentClient.CreateDb(_dbName);
        Assert.Empty(await read.WebhookEvents.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(_published);
    }

    [Fact]
    public async Task InvalidSignature_FailsAndStoresNothing()
    {
        await SeedAccountAsync();
        _gateway.Rejection = new Error("StripeConnect.Webhook.InvalidSignature", "Bad signature.");

        var result = await HandleAsync();

        Assert.Equal("StripeConnect.Webhook.InvalidSignature", result.Error.Code);
        await using var read = InMemoryPaymentClient.CreateDb(_dbName);
        Assert.Empty(await read.WebhookEvents.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task DuplicateEvent_IsSkipped_WithoutPublishingAgain()
    {
        await SeedAccountAsync();
        await SeedScheduleAsync();

        await DeliverPayoutAsync("evt_d", "payout.paid", "po_7", 700, "usd", null);
        await DeliverPayoutAsync("evt_d", "payout.paid", "po_7", 700, "usd", null);

        Assert.Single(_published);
        await using var read = InMemoryPaymentClient.CreateDb(_dbName);
        Assert.Single((await read.PayoutSchedules.IgnoreQueryFilters().Include(s => s.Items).SingleAsync()).Items);
    }

    [Fact]
    public async Task EventLeftHalfDone_IsReprocessedOnTheNextDelivery()
    {
        await SeedAccountAsync();
        await SeedScheduleAsync();
        await using (var db = InMemoryPaymentClient.CreateDb(_dbName))
        {
            var received = WebhookEvent
                .Receive(_tenantId, PaymentProviderCode.Stripe, "evt_r", "payout.paid", "{}", "sig", DateTime.UtcNow)
                .Value;
            received.MarkProcessing(DateTime.UtcNow);
            received.MarkFailed("transient", DateTime.UtcNow);
            db.WebhookEvents.Add(received);
            await db.SaveChangesAsync();
        }

        await DeliverPayoutAsync("evt_r", "payout.paid", "po_8", 700, "usd", null);

        Assert.Single(_published);
        Assert.Equal(WebhookEventStatus.Applied, await StatusOfAsync("evt_r"));
    }

    [Fact]
    public async Task PayoutEvent_SaveFails_PublishesNothing()
    {
        await SeedAccountAsync();
        await SeedScheduleAsync();
        _gateway.Next = Payout("evt_f", "payout.paid", "po_9", 700, "usd", null);

        await Assert.ThrowsAsync<DbUpdateException>(() => HandleAsync(new FailingUnitOfWork()));

        Assert.Empty(_published);
    }

    [Fact]
    public async Task AccountEvent_SaveFails_PublishesNothing()
    {
        await SeedAccountAsync();
        _gateway.Next = Account("evt_f", chargesEnabled: true, [], null);

        await Assert.ThrowsAsync<DbUpdateException>(() => HandleAsync(new FailingUnitOfWork()));

        Assert.Empty(_published);
    }

    private Task DeliverAccountAsync(
        string eventId,
        bool chargesEnabled,
        IReadOnlyList<string> requirements,
        DateTime? occurredAtUtc = null
    )
    {
        _gateway.Next = Account(eventId, chargesEnabled, requirements, occurredAtUtc);
        return InvokeAsync();
    }

    private static ConnectWebhookEvent Account(
        string eventId,
        bool chargesEnabled,
        IReadOnlyList<string> requirements,
        DateTime? occurredAtUtc
    ) =>
        new(
            eventId,
            "account.updated",
            AccountId,
            ChargesEnabled: chargesEnabled,
            PayoutsEnabled: chargesEnabled,
            RequirementsCurrentlyDue: requirements,
            PayoutReference: null,
            PayoutAmountCents: null,
            PayoutCurrency: null,
            PayoutFailureReason: null,
            OccurredAtUtc: occurredAtUtc
        );

    private Task DeliverPayoutAsync(
        string eventId,
        string eventType,
        string? reference,
        long? amountCents,
        string? currency,
        string? failureReason
    )
    {
        _gateway.Next = Payout(eventId, eventType, reference, amountCents, currency, failureReason);
        return InvokeAsync();
    }

    private static ConnectWebhookEvent Payout(
        string eventId,
        string eventType,
        string? reference,
        long? amountCents,
        string? currency,
        string? failureReason
    ) =>
        new(
            eventId,
            eventType,
            AccountId,
            ChargesEnabled: null,
            PayoutsEnabled: null,
            RequirementsCurrentlyDue: null,
            PayoutReference: reference,
            PayoutAmountCents: amountCents,
            PayoutCurrency: currency,
            PayoutFailureReason: failureReason
        );

    private async Task<WebhookEventStatus> StatusOfAsync(string eventId)
    {
        await using var read = InMemoryPaymentClient.CreateDb(_dbName);
        return (await read.WebhookEvents.IgnoreQueryFilters().SingleAsync(e => e.ProviderEventId == eventId)).Status;
    }

    private async Task SeedConnectConfigAsync()
    {
        _db.TenantPaymentConfigs.Add(
            TenantPaymentConfig
                .Create(
                    _tenantId,
                    PaymentProviderCode.Stripe,
                    TenantPaymentMode.Connect,
                    "pk_test",
                    StatementDescriptor.Create("ACME TAX SVC").Value,
                    DateTime.UtcNow
                )
                .Value
        );
        await _db.SaveChangesAsync();
    }

    private async Task<Guid> SeedScheduleAsync()
    {
        var account = await _db.TenantConnectAccounts.IgnoreQueryFilters().SingleAsync();
        var schedule = PayoutSchedule
            .Create(_tenantId, account.Id, PayoutFrequency.Weekly, 1, "USD", Guid.Empty, DateTime.UtcNow)
            .Value;
        _db.PayoutSchedules.Add(schedule);
        await _db.SaveChangesAsync();
        return schedule.Id;
    }

    private sealed class FailingUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
            throw new DbUpdateException("Simulated save failure.");
    }

    private sealed class CountingMetrics : IPaymentClientMetrics
    {
        public int OnboardingCompleted { get; private set; }

        public void RecordConnectOnboardingCompleted() => OnboardingCompleted++;

        public void RecordPaymentSucceeded(long amountCents, string currency) { }

        public void RecordPlatformFee(long feeCents, string currency) { }

        public void RecordPaymentLinkCreated() { }

        public void RecordPaymentLinkUsed() { }

        public void RecordRefund(string provider) { }

        public void RecordWebhookReceived(string provider) { }

        public void RecordWebhookDuplicate(string provider) { }

        public void RecordWebhookSignatureFailed(string provider) { }

        public void RecordWebhookNeedsReview(string provider, string reason) { }
    }
}
