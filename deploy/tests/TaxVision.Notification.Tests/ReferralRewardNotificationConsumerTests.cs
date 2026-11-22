using BuildingBlocks.Common;
using BuildingBlocks.Messaging.GrowthIntegrationEvents;
using BuildingBlocks.Messaging.NotificationIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

/// <summary>
/// El owner de la oficina referidora se entera de su premio: calificado (en espera), acreditado (campana y correo),
/// cancelado antes de acreditarse y revertido. Cada aviso dice el monto y se agrupa por premio.
/// </summary>
public sealed class ReferralRewardNotificationConsumerTests
{
    private static readonly Guid Office = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid Owner = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid RewardCaseId = Guid.Parse("50000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Qualified_tells_the_owner_the_amount_and_the_waiting_period()
    {
        var h = new Harness();

        await ReferralQualifiedNotificationConsumer.Handle(
            Qualified(),
            h.Dispatcher.Dispatcher,
            h.Resolver,
            h.Correlation,
            default
        );

        var row = Assert.Single(h.InAppRows);
        Assert.Equal(Owner, row.RecipientUserId);
        Assert.Equal(NotificationKinds.ReferralQualified, row.Kind);
        Assert.Equal("Your referral paid their plan", row.FallbackTitle);
        Assert.Contains("$9.90", row.FallbackBody);
        Assert.Contains("after the waiting period", row.FallbackBody);
        Assert.Equal($"referrals:reward:{RewardCaseId}:qualified", row.GroupKey);
    }

    [Fact]
    public async Task Granted_sends_the_email_and_the_in_app_notice()
    {
        var h = new Harness(host: "acme.taxoffix.io");
        var evt = Granted();

        await h.GrantedAsync(evt);

        Assert.Equal(ReferralRewardGrantedNotificationConsumer.EmailEventKey, h.Scribe.LastEventKey);
        Assert.Equal("$9.90", h.Scribe.LastVariables["amount_label"]);
        Assert.Equal("https://acme.taxoffix.io/wallet", h.Scribe.LastVariables["wallet_url"]);
        var email = Assert.Single(h.Gateway.Queued);
        Assert.Equal("owner@office.test", email.To);
        Assert.Equal(evt.EventId, email.RelatedEventId);
        // Destinatario + categoría activan el gate de preferencias del correo.
        Assert.Equal(Owner, email.RecipientUserId);
        Assert.Equal(NotificationCategory.Billing, email.Category);
        Assert.Equal(NotificationKinds.ReferralRewardGranted, email.NotificationKind);

        var row = Assert.Single(h.InAppRows);
        Assert.Equal("Your referral reward is in your Wallet", row.FallbackTitle);
        Assert.Contains("$9.90 was added", row.FallbackBody);
    }

    [Theory]
    [InlineData("ReferredPaymentRefunded", "was refunded")]
    [InlineData("ReferredPaymentDisputeLost", "was disputed")]
    [InlineData("ReviewRejected", "was reviewed")]
    public async Task Rejected_explains_why_the_reward_will_not_be_paid(string reasonCode, string expected)
    {
        var h = new Harness();

        await ReferralRewardRejectedNotificationConsumer.Handle(
            Rejected(reasonCode),
            h.Dispatcher.Dispatcher,
            h.Resolver,
            h.Correlation,
            default
        );

        var row = Assert.Single(h.InAppRows);
        Assert.Equal(NotificationKinds.ReferralRewardRejected, row.Kind);
        Assert.Equal("A referral reward was cancelled", row.FallbackTitle);
        Assert.Contains(expected, row.FallbackBody);
    }

    [Fact]
    public async Task Reversed_tells_the_owner_the_amount_was_removed()
    {
        var h = new Harness();

        await ReferralRewardReversedNotificationConsumer.Handle(
            Reversed(),
            h.Dispatcher.Dispatcher,
            h.Resolver,
            h.Correlation,
            default
        );

        var row = Assert.Single(h.InAppRows);
        Assert.Equal(NotificationKinds.ReferralRewardReversed, row.Kind);
        Assert.Contains("$9.90 was removed from your Wallet", row.FallbackBody);
    }

    [Fact]
    public async Task A_redelivered_event_keeps_the_same_notice_and_email_keys()
    {
        var h = new Harness();
        var evt = Granted();

        await h.GrantedAsync(evt);
        await h.GrantedAsync(evt);

        Assert.Single(h.InAppRows.Select(row => row.GroupKey).Distinct());
        Assert.Single(h.Gateway.Queued.Select(email => email.RelatedEventId).Distinct());
    }

    [Fact]
    public async Task An_office_without_owner_gets_nothing()
    {
        var h = new Harness(withOwner: false);

        await h.GrantedAsync(Granted());

        Assert.Empty(h.InAppRows);
        Assert.Empty(h.Gateway.Queued);
    }

    [Fact]
    public void The_catalog_sends_email_only_for_a_granted_reward()
    {
        Assert.Contains(
            NotificationChannel.Email,
            NotificationCatalog.GetRequired(NotificationKinds.ReferralRewardGranted).AllowedChannels
        );
        foreach (
            var kind in new[]
            {
                NotificationKinds.ReferralQualified,
                NotificationKinds.ReferralRewardRejected,
                NotificationKinds.ReferralRewardReversed,
            }
        )
        {
            var entry = NotificationCatalog.GetRequired(kind);
            Assert.Equal(NotificationCatalogCategory.Billing, entry.Category);
            Assert.DoesNotContain(NotificationChannel.Email, entry.AllowedChannels);
        }
    }

    private static ReferralQualifiedIntegrationEvent Qualified() =>
        new()
        {
            TenantId = Office,
            AttributionId = Guid.NewGuid(),
            QualificationId = Guid.NewGuid(),
            ProgramType = "TenantToTenant",
            QualifyingPaymentId = Guid.NewGuid(),
            AggregateId = RewardCaseId,
            RewardAmountCents = 990,
            RewardCurrency = "USD",
            RewardAvailableAtUtc = new DateTime(2026, 12, 17, 0, 0, 0, DateTimeKind.Utc),
        };

    private static ReferralRewardGrantedIntegrationEvent Granted() =>
        new()
        {
            TenantId = Office,
            AggregateId = RewardCaseId,
            RewardCaseId = RewardCaseId,
            GrantId = Guid.NewGuid(),
            BenefitType = "WalletCredit",
            MaterializedBy = "Wallet",
            MaterializedReferenceId = Guid.NewGuid(),
            RewardAmountCents = 990,
            RewardCurrency = "USD",
        };

    private static ReferralRewardRejectedIntegrationEvent Rejected(string reasonCode) =>
        new()
        {
            TenantId = Office,
            AggregateId = RewardCaseId,
            RewardCaseId = RewardCaseId,
            GrantId = Guid.NewGuid(),
            ReasonCode = reasonCode,
            RewardAmountCents = 990,
            RewardCurrency = "USD",
        };

    private static ReferralRewardReversedIntegrationEvent Reversed() =>
        new()
        {
            TenantId = Office,
            AggregateId = RewardCaseId,
            RewardCaseId = RewardCaseId,
            GrantId = Guid.NewGuid(),
            SourceFinancialEventId = Guid.NewGuid(),
            ReasonCode = "ReferredPaymentReversed",
            RewardAmountCents = 990,
            RewardCurrency = "USD",
        };

    private sealed class Harness(string? host = null, bool withOwner = true)
    {
        public RecordingScribeRenderClient Scribe { get; } = new();
        public RecordingEmailGateway Gateway { get; } = new();
        public OwnerResolver Resolver { get; } = new(withOwner);
        public PassThroughCorrelation Correlation { get; } = new();

        // El correo sale por el dispatcher: después del gateway y Scribe.
        public NotificationDispatcherHarness Dispatcher => dispatcher ??= new(emailGateway: Gateway, scribe: Scribe);
        private NotificationDispatcherHarness? dispatcher;

        public IReadOnlyList<InAppNotificationRequestedIntegrationEvent> InAppRows =>
            Dispatcher.Publisher.Published.OfType<InAppNotificationRequestedIntegrationEvent>().ToArray();

        public Task GrantedAsync(ReferralRewardGrantedIntegrationEvent evt) =>
            ReferralRewardGrantedNotificationConsumer.Handle(
                evt,
                Dispatcher.Dispatcher,
                Resolver,
                new OwnerEmail(),
                Scribe,
                new FakeTenantHostResolver(host),
                Options.Create(new PortalOptions()),
                FixedRecipientLocale.English,
                Correlation,
                NullLogger<ReferralRewardGrantedIntegrationEvent>.Instance,
                default
            );
    }

    private sealed class OwnerResolver(bool withOwner) : IRecipientResolver
    {
        public Task<IReadOnlyList<Guid>> ResolveAsync(ByPermission audience, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<Result<IReadOnlyList<Guid>>> ResolveAsync(
            NotificationAudience audience,
            RecipientValidation validation,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                Result.Success<IReadOnlyList<Guid>>(
                    audience is TenantOwnerAudience { TenantId: var tenant } && tenant == Office && withOwner
                        ? [Owner]
                        : []
                )
            );
    }

    private sealed class OwnerEmail : IUserEmailResolver
    {
        public Task<string?> ResolveAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult<string?>(userId == Owner ? "owner@office.test" : null);
    }

    private sealed class PassThroughCorrelation : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = string.Empty;

        public void Set(string correlationId) => CorrelationId = correlationId;

        public IDisposable Push(string correlationId)
        {
            CorrelationId = correlationId;
            return new Scope();
        }

        private sealed class Scope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
