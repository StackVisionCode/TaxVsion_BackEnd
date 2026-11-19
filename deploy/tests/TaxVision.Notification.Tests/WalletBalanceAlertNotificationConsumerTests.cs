using BuildingBlocks.Common;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Messaging.NotificationIntegrationEvents;
using BuildingBlocks.Messaging.WalletIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;
using TaxVision.Notification.Domain.Recipients;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

/// <summary>
/// Avisos del Wallet (saldo bajo y recarga automática): correo y campana a quien tiene <c>wallet.manage</c>; sin nadie
/// con el permiso, al owner y los admins. Un correo por persona y evento; una reentrega no repite el envío.
/// </summary>
public sealed class WalletBalanceAlertNotificationConsumerTests
{
    private static readonly Guid Office = Guid.Parse("20000000-0000-0000-0000-000000000003");
    private static readonly Guid Manager = Guid.Parse("11111111-1111-4111-8111-111111111113");
    private static readonly Guid CoManager = Guid.Parse("22222222-2222-4222-8222-222222222223");
    private static readonly Guid Owner = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Admin = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid TopUpId = Guid.Parse("70000000-0000-0000-0000-000000000001");
    private static readonly DateTime ChargedAt = new(2026, 11, 18, 15, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Low_balance_emails_and_rings_everyone_with_wallet_manage()
    {
        var h = new Harness(managers: [Manager, CoManager], host: "acme.taxoffix.io");
        var evt = LowBalance();

        await h.LowBalanceAsync(evt);

        var call = Assert.Single(h.Scribe.Calls);
        Assert.Equal(WalletLowBalanceNotificationConsumer.EmailEventKey, call.EventKey);
        Assert.Equal("$3.20", call.Variables["available_label"]);
        Assert.Equal("$5.00", call.Variables["threshold_label"]);
        Assert.Equal("https://acme.taxoffix.io/wallet", call.Variables["wallet_url"]);
        Assert.Equal("https://acme.taxoffix.io/settings/wallet", call.Variables["settings_url"]);

        Assert.Equal(
            new[] { Harness.EmailOf(Manager), Harness.EmailOf(CoManager) }.Order(),
            h.Postmaster.Select(email => email.To).Order()
        );
        var byPermission = Assert.Single(h.Resolver.Audiences.OfType<ByPermission>());
        Assert.Equal("wallet.manage", byPermission.PermissionCode);
        var email = h.Queued[0];
        Assert.Equal(NotificationCategory.Billing, email.Category);
        Assert.Equal(NotificationKinds.WalletLowBalance, email.NotificationKind);

        Assert.Equal(new[] { Manager, CoManager }.Order(), h.InAppRows.Select(row => row.RecipientUserId).Order());
        var row = h.InAppRows[0];
        Assert.Equal(NotificationKinds.WalletLowBalance, row.Kind);
        Assert.Equal("Your wallet balance is low", row.FallbackTitle);
        Assert.Contains("$3.20", row.FallbackBody);
        Assert.Contains("$5.00", row.FallbackBody);
        Assert.Equal("wallet", row.ResourceType);
        Assert.Equal("Billing", row.Category);
    }

    [Fact]
    public async Task Each_email_key_names_the_person_and_the_event()
    {
        var h = new Harness(managers: [Manager, CoManager]);
        var evt = LowBalance();

        await h.LowBalanceAsync(evt);

        var keys = h.Postmaster.Select(email => email.IdempotencyKey).ToArray();
        Assert.Equal(2, keys.Distinct().Count());
        Assert.All(
            h.Postmaster,
            email =>
            {
                Assert.Contains(evt.EventId.ToString("N"), email.IdempotencyKey);
                Assert.Contains(email.To.ToLowerInvariant(), email.IdempotencyKey);
            }
        );
    }

    [Fact]
    public async Task A_redelivery_does_not_send_the_email_again()
    {
        var h = new Harness(managers: [Manager, CoManager]);
        var evt = LowBalance();

        await h.LowBalanceAsync(evt);
        await h.LowBalanceAsync(evt);

        Assert.Equal(2, h.Postmaster.Count);
        // La campana se republica con el mismo evento y grupo: Communication la actualiza sin duplicar.
        Assert.Equal(
            2,
            h.InAppRows.Select(row => (row.RecipientUserId, row.GroupKey, row.SourceEventId)).Distinct().Count()
        );
    }

    [Fact]
    public async Task Without_wallet_managers_the_owner_and_admins_are_told_once_each()
    {
        var h = new Harness(managers: [], owner: Owner, admins: [Owner, Admin]);

        await h.LowBalanceAsync(LowBalance());

        Assert.Equal(new[] { Owner, Admin }.Order(), h.InAppRows.Select(row => row.RecipientUserId).Order());
        Assert.Equal(
            new[] { Harness.EmailOf(Owner), Harness.EmailOf(Admin) }.Order(),
            h.Postmaster.Select(email => email.To).Order()
        );
    }

    [Fact]
    public async Task Auto_recharge_receipt_shows_amount_method_balance_and_office_time()
    {
        var h = new Harness(managers: [Manager]);
        var evt = Succeeded();

        await h.SucceededAsync(evt);
        await h.SucceededAsync(evt);

        var call = h.Scribe.Calls[0];
        Assert.Equal(WalletAutoRechargeSucceededNotificationConsumer.EmailEventKey, call.EventKey);
        Assert.Equal("$25.00", call.Variables["amount_label"]);
        Assert.Equal("Visa •••• 4242", call.Variables["method_label"]);
        Assert.Equal("$27.50", call.Variables["balance_after_label"]);
        Assert.Equal("2026-11-18T15:30:00.0000000Z", TypedDates.Instant(call.Variables["charged_at"]));
        Assert.Equal("http://localhost:4200/wallet", call.Variables["wallet_url"]);

        var sent = Assert.Single(h.Postmaster);
        Assert.Contains(TopUpId.ToString("N"), sent.IdempotencyKey);
        Assert.Contains(Harness.EmailOf(Manager), sent.IdempotencyKey);

        var row = h.InAppRows[0];
        Assert.Equal(NotificationKinds.WalletAutoRechargeSucceeded, row.Kind);
        Assert.Equal("Automatic recharge completed", row.FallbackTitle);
        Assert.Contains("$25.00", row.FallbackBody);
        Assert.Contains("Visa •••• 4242", row.FallbackBody);
        Assert.Equal("wallet", row.ResourceType);
        Assert.Equal(TopUpId.ToString(), row.ResourceId);
    }

    [Theory]
    [InlineData("PaymentFailed", "couldn't charge Visa •••• 4242")]
    [InlineData("MethodUnavailable", "Visa •••• 4242 is no longer available")]
    public async Task Paused_explains_why_and_points_to_settings(string reason, string fragment)
    {
        var h = new Harness(managers: [Manager], host: "acme.taxoffix.io");

        await h.PausedAsync(Paused(reason));

        var call = Assert.Single(h.Scribe.Calls);
        Assert.Equal(WalletAutoRechargePausedNotificationConsumer.EmailEventKey, call.EventKey);
        Assert.Contains(fragment, (string)call.Variables["reason_text"]!);
        Assert.Equal("$25.00", call.Variables["amount_label"]);
        Assert.Equal("https://acme.taxoffix.io/settings/wallet", call.Variables["settings_url"]);
        Assert.Single(h.Postmaster);

        var row = Assert.Single(h.InAppRows);
        Assert.Equal(NotificationKinds.WalletAutoRechargePaused, row.Kind);
        Assert.Equal("Automatic recharge is paused", row.FallbackTitle);
        Assert.Contains(fragment, row.FallbackBody);
        Assert.Contains("won't retry on its own", row.FallbackBody);
        Assert.Contains("Settings → Wallet", row.FallbackBody);
        Assert.Equal("wallet_settings", row.ResourceType);
    }

    [Fact]
    public async Task Paused_without_top_up_keys_the_email_by_event()
    {
        var h = new Harness(managers: [Manager]);
        var evt = Paused("MethodUnavailable") with { TopUpId = null };

        await h.PausedAsync(evt);

        var sent = Assert.Single(h.Postmaster);
        Assert.Contains(evt.EventId.ToString("N"), sent.IdempotencyKey);
        Assert.Contains(Harness.EmailOf(Manager), sent.IdempotencyKey);
    }

    [Fact]
    public async Task Daily_limit_says_it_continues_tomorrow()
    {
        var h = new Harness(managers: [Manager]);

        await h.DailyLimitAsync(DailyLimit());

        var call = Assert.Single(h.Scribe.Calls);
        Assert.Equal(WalletAutoRechargeDailyLimitReachedNotificationConsumer.EmailEventKey, call.EventKey);
        Assert.Equal("3", call.Variables["daily_limit"]);
        Assert.Equal("$25.00", call.Variables["amount_label"]);
        Assert.Single(h.Postmaster);

        var row = Assert.Single(h.InAppRows);
        Assert.Equal(NotificationKinds.WalletAutoRechargeDailyLimitReached, row.Kind);
        Assert.Equal("Daily automatic recharge limit reached", row.FallbackTitle);
        Assert.Contains("3 automatic recharges", row.FallbackBody);
        Assert.Contains("tomorrow", row.FallbackBody);
        Assert.Equal("wallet_settings", row.ResourceType);
    }

    [Fact]
    public async Task Without_email_addresses_only_the_bell_rings()
    {
        var h = new Harness(managers: [Manager], withEmails: false);

        await h.LowBalanceAsync(LowBalance());

        Assert.Empty(h.Scribe.Calls);
        Assert.Empty(h.Postmaster);
        Assert.Single(h.InAppRows);
    }

    [Theory]
    [InlineData(NotificationKinds.WalletLowBalance)]
    [InlineData(NotificationKinds.WalletAutoRechargeSucceeded)]
    [InlineData(NotificationKinds.WalletAutoRechargePaused)]
    [InlineData(NotificationKinds.WalletAutoRechargeDailyLimitReached)]
    public void The_catalog_sends_wallet_alerts_by_bell_and_email(string kind)
    {
        var entry = NotificationCatalog.GetRequired(kind);

        Assert.Equal(NotificationCatalogCategory.Billing, entry.Category);
        Assert.Equal(NotificationRecipientPolicy.ByPermission, entry.RecipientPolicy);
        Assert.Contains(NotificationChannel.Email, entry.AllowedChannels);
        Assert.Contains(NotificationChannel.InApp, entry.AllowedChannels);
    }

    [Fact]
    public void Receipts_and_pauses_cannot_be_turned_off()
    {
        Assert.True(
            NotificationCatalog
                .GetRequired(NotificationKinds.WalletAutoRechargeSucceeded)
                .IsMandatoryFor(NotificationChannel.Email)
        );
        Assert.True(
            NotificationCatalog
                .GetRequired(NotificationKinds.WalletAutoRechargePaused)
                .IsMandatoryFor(NotificationChannel.Email)
        );
        Assert.False(
            NotificationCatalog
                .GetRequired(NotificationKinds.WalletLowBalance)
                .IsMandatoryFor(NotificationChannel.Email)
        );
    }

    private static WalletLowBalanceIntegrationEvent LowBalance() =>
        new()
        {
            TenantId = Office,
            CorrelationId = "corr-wallet",
            AvailableMicros = 3_200_000,
            ThresholdMicros = 5_000_000,
            Currency = "USD",
        };

    private static WalletAutoRechargeSucceededIntegrationEvent Succeeded() =>
        new()
        {
            TenantId = Office,
            OccurredOn = ChargedAt,
            TopUpId = TopUpId,
            AmountCents = 2_500,
            Currency = "USD",
            MethodLabel = "Visa •••• 4242",
            AvailableAfterMicros = 27_500_000,
        };

    private static WalletAutoRechargePausedIntegrationEvent Paused(string reason) =>
        new()
        {
            TenantId = Office,
            TopUpId = TopUpId,
            Reason = reason,
            AmountCents = 2_500,
            Currency = "USD",
            MethodLabel = "Visa •••• 4242",
        };

    private static WalletAutoRechargeDailyLimitReachedIntegrationEvent DailyLimit() =>
        new()
        {
            TenantId = Office,
            DailyLimit = 3,
            AmountCents = 2_500,
            Currency = "USD",
        };

    private sealed class Harness
    {
        private readonly NotificationDispatcherHarness _dispatcher;
        private readonly MemoryLogRepository _logs = new();
        private readonly CapturingIntegrationEventPublisher _gatewayPublisher = new();
        private readonly IEmailDispatchGateway _gateway;
        private readonly FakeOffices _offices;
        private readonly FakeEmails _emails;
        private readonly ITenantHostResolver _hosts;

        public Harness(
            IReadOnlyList<Guid> managers,
            Guid? owner = null,
            IReadOnlyList<Guid>? admins = null,
            string? host = null,
            bool withEmails = true
        )
        {
            Resolver = new FakeResolver(managers, owner);
            _offices = new FakeOffices(admins ?? []);
            _emails = new FakeEmails(withEmails);
            _hosts = new FakeTenantHostResolver(host);
            _dispatcher = new NotificationDispatcherHarness(logs: _logs);
            var real = new EventBasedEmailDispatchGateway(
                _gatewayPublisher,
                _logs,
                new NoOpUnitOfWork(),
                TestDeliveryGates.Open(),
                NullLogger<EventBasedEmailDispatchGateway>.Instance
            );
            _gateway = new RecordingGateway(real, Queued);
        }

        public FakeResolver Resolver { get; }
        public RecordingScribe Scribe { get; } = new();
        public List<EmailDispatchRequest> Queued { get; } = [];

        public IReadOnlyList<InAppNotificationRequestedIntegrationEvent> InAppRows =>
            _dispatcher.Publisher.Published.OfType<InAppNotificationRequestedIntegrationEvent>().ToArray();

        public IReadOnlyList<NotificationsEmailSendRequestedIntegrationEvent> Postmaster =>
            _gatewayPublisher.Published.OfType<NotificationsEmailSendRequestedIntegrationEvent>().ToArray();

        public static string EmailOf(Guid userId) => $"{userId:N}@office.test";

        private readonly PassThroughCorrelation _correlation = new();

        private WalletAlertSender Sender() =>
            new(
                _dispatcher.Dispatcher,
                Resolver,
                _offices,
                _emails,
                _gateway,
                Scribe,
                _hosts,
                Options.Create(new PortalOptions()),
                _correlation
            );

        public Task LowBalanceAsync(WalletLowBalanceIntegrationEvent evt) =>
            WalletLowBalanceNotificationConsumer.Handle(evt, Sender(), _correlation, CancellationToken.None);

        public Task SucceededAsync(WalletAutoRechargeSucceededIntegrationEvent evt) =>
            WalletAutoRechargeSucceededNotificationConsumer.Handle(evt, Sender(), _correlation, CancellationToken.None);

        public Task PausedAsync(WalletAutoRechargePausedIntegrationEvent evt) =>
            WalletAutoRechargePausedNotificationConsumer.Handle(evt, Sender(), _correlation, CancellationToken.None);

        public Task DailyLimitAsync(WalletAutoRechargeDailyLimitReachedIntegrationEvent evt) =>
            WalletAutoRechargeDailyLimitReachedNotificationConsumer.Handle(
                evt,
                Sender(),
                _correlation,
                CancellationToken.None
            );
    }

    /// <summary>wallet.manage devuelve los managers; el owner sale de su audiencia.</summary>
    private sealed class FakeResolver(IReadOnlyList<Guid> managers, Guid? owner) : IRecipientResolver
    {
        public List<NotificationAudience> Audiences { get; } = [];

        public Task<IReadOnlyList<Guid>> ResolveAsync(ByPermission audience, CancellationToken ct = default) =>
            Task.FromResult(audience.PermissionCode == "wallet.manage" ? managers : (IReadOnlyList<Guid>)[]);

        public Task<Result<IReadOnlyList<Guid>>> ResolveAsync(
            NotificationAudience audience,
            RecipientValidation validation,
            CancellationToken ct = default
        )
        {
            Audiences.Add(audience);
            IReadOnlyList<Guid> ids = audience switch
            {
                ByPermission { PermissionCode: "wallet.manage", TenantId: var tenant } when tenant == Office =>
                    managers,
                TenantOwnerAudience when owner is { } id => [id],
                _ => [],
            };
            return Task.FromResult(Result.Success(ids));
        }
    }

    private sealed class FakeOffices(IReadOnlyList<Guid> admins) : ITenantOwnerProjectionRepository
    {
        public Task<Guid?> GetActiveOwnerUserIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<Guid?>(null);

        public Task<IReadOnlyList<Guid>> GetActiveAdminUserIdsAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(tenantId == Office ? admins : []);

        public Task<TenantOwnerProjection?> GetAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult<TenantOwnerProjection?>(null);

        public Task AddAsync(TenantOwnerProjection projection, CancellationToken ct = default) => Task.CompletedTask;

        public Task<TenantOwnerProjection?> GetByUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult<TenantOwnerProjection?>(null);
    }

    private sealed class FakeEmails(bool withEmails) : IUserEmailResolver
    {
        public Task<string?> ResolveAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult(withEmails ? Harness.EmailOf(userId) : null);
    }

    private sealed class RecordingScribe : IScribeRenderClient
    {
        public List<(string EventKey, IReadOnlyDictionary<string, object?> Variables)> Calls { get; } = [];

        public Task<Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            ScribeRenderAudience audience,
            CancellationToken ct = default
        ) => RenderAsync(eventKey, tenantId, variables, ct);

        public Task<Result<ScribeRenderedText>> RenderTextAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        ) => ScribeTestDefaults.TextUnsupported();

        public Task<Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        )
        {
            Calls.Add((eventKey, variables));
            return Task.FromResult(Result.Success(new ScribeRenderedEmail("Subject", "<p>html</p>", "text")));
        }
    }

    /// <summary>Envuelve el gateway real (idempotencia incluida) y registra lo que pidió el consumer.</summary>
    private sealed class RecordingGateway(IEmailDispatchGateway inner, List<EmailDispatchRequest> queued)
        : IEmailDispatchGateway
    {
        public async Task<EmailDispatchResult> QueueEmailAsync(
            EmailDispatchRequest request,
            CancellationToken ct = default
        )
        {
            queued.Add(request);
            return await inner.QueueEmailAsync(request, ct);
        }
    }

    private sealed class MemoryLogRepository : INotificationLogRepository
    {
        private readonly List<NotificationLog> _logs = [];

        public Task AddAsync(NotificationLog log, CancellationToken ct = default)
        {
            _logs.Add(log);
            return Task.CompletedTask;
        }

        public Task<(IReadOnlyList<NotificationLog> Items, int TotalCount)> GetPagedAsync(
            Guid tenantId,
            NotificationStatus? status,
            int page,
            int size,
            CancellationToken ct = default
        ) => Task.FromResult<(IReadOnlyList<NotificationLog>, int)>((_logs, _logs.Count));

        public Task<NotificationLog?> GetByRelatedEventIdAsync(
            Guid tenantId,
            Guid relatedEventId,
            string templateKey,
            string recipient,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                _logs.FirstOrDefault(log =>
                    log.Channel == NotificationChannel.Email
                    && log.RelatedEventId == relatedEventId
                    && log.TemplateKey == templateKey
                    && string.Equals(log.Recipient, recipient, StringComparison.OrdinalIgnoreCase)
                )
            );

        public Task<NotificationLog?> GetByRelatedEventAsync(
            Guid tenantId,
            Guid relatedEventId,
            string kind,
            NotificationChannel channel,
            string recipient,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                _logs.LastOrDefault(log =>
                    log.Channel == channel
                    && log.RelatedEventId == relatedEventId
                    && log.Kind == kind
                    && log.Recipient == recipient
                )
            );
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
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
