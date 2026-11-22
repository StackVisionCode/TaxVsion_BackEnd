using BuildingBlocks.Common;
using BuildingBlocks.Messaging.NotificationIntegrationEvents;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
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
/// Un descuento de N meses termina en 7 días: el owner recibe campana y correo una sola vez, con el beneficio y cuándo
/// vuelve el precio normal.
/// </summary>
public sealed class SubscriptionDiscountEndingNotificationConsumerTests
{
    private static readonly Guid Office = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid Owner = Guid.Parse("11111111-1111-4111-8111-111111111112");
    private static readonly Guid DiscountId = Guid.Parse("60000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Discount_ending_notifies_the_owner_once()
    {
        var h = new Harness(host: "acme.taxoffix.io");
        var evt = Ending(percent: 2_000, amountCents: null);

        await h.HandleAsync(evt);
        await h.HandleAsync(evt);

        Assert.Equal(SubscriptionDiscountEndingNotificationConsumer.EmailEventKey, h.Scribe.LastEventKey);
        Assert.Equal("20%", h.Scribe.LastVariables["discount_label"]);
        Assert.Equal("7", h.Scribe.LastVariables["days_left"]);
        Assert.Equal("https://acme.taxoffix.io", h.Scribe.LastVariables["billing_url"]);
        var email = h.Gateway.Queued[0];
        Assert.Equal("owner@office.test", email.To);
        Assert.Equal(Owner, email.RecipientUserId);
        Assert.Equal(NotificationCategory.Billing, email.Category);
        Assert.Equal(NotificationKinds.SubscriptionDiscountEnding, email.NotificationKind);
        Assert.Single(h.Gateway.Queued.Select(queued => queued.RelatedEventId).Distinct());

        var row = h.InAppRows[0];
        Assert.Equal(Owner, row.RecipientUserId);
        Assert.Equal(NotificationKinds.SubscriptionDiscountEnding, row.Kind);
        Assert.Equal("Your discount ends soon", row.FallbackTitle);
        Assert.Contains("Your 20% discount ends soon. Days left: 7.", row.FallbackBody);
        Assert.Equal($"subscription:discount:{DiscountId}:ending", row.GroupKey);
        Assert.Single(h.InAppRows.Select(r => r.GroupKey).Distinct());
    }

    [Fact]
    public async Task A_fixed_discount_shows_its_amount()
    {
        var h = new Harness();

        await h.HandleAsync(Ending(percent: null, amountCents: 500));

        Assert.Equal("$5.00", h.Scribe.LastVariables["discount_label"]);
        Assert.Contains("Your $5.00 discount ends soon. Days left: 7.", h.InAppRows[0].FallbackBody);
    }

    [Fact]
    public async Task An_office_without_owner_gets_nothing()
    {
        var h = new Harness(withOwner: false);

        await h.HandleAsync(Ending(percent: 2_000, amountCents: null));

        Assert.Empty(h.InAppRows);
        Assert.Empty(h.Gateway.Queued);
    }

    [Fact]
    public void The_catalog_sends_it_to_the_owner_by_bell_and_email()
    {
        var entry = NotificationCatalog.GetRequired(NotificationKinds.SubscriptionDiscountEnding);

        Assert.Equal(NotificationCatalogCategory.Billing, entry.Category);
        Assert.Contains(NotificationChannel.Email, entry.AllowedChannels);
        Assert.Contains(NotificationChannel.InApp, entry.AllowedChannels);
    }

    private static SubscriptionDiscountEndingIntegrationEvent Ending(int? percent, long? amountCents) =>
        new()
        {
            TenantId = Office,
            TenantSubscriptionId = Guid.NewGuid(),
            DiscountId = DiscountId,
            EndsAtUtc = new DateTime(2027, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            DaysUntilEnd = 7,
            Source = "Code",
            PercentBasisPoints = percent,
            AmountCents = amountCents,
            Currency = amountCents is null ? null : "USD",
        };

    private sealed class Harness(string? host = null, bool withOwner = true)
    {
        public RecordingEmailGateway Gateway { get; } = new();
        private NotificationDispatcherHarness? dispatcher;

        // El correo sale por el punto único, que encola en este gateway.
        public NotificationDispatcherHarness Dispatcher => dispatcher ??= new(emailGateway: Gateway);
        public OwnerResolver Resolver { get; } = new(withOwner);
        public RecordingScribeRenderClient Scribe { get; } = new();
        public PassThroughCorrelation Correlation { get; } = new();

        public IReadOnlyList<InAppNotificationRequestedIntegrationEvent> InAppRows =>
            Dispatcher.Publisher.Published.OfType<InAppNotificationRequestedIntegrationEvent>().ToArray();

        public Task HandleAsync(SubscriptionDiscountEndingIntegrationEvent evt) =>
            SubscriptionDiscountEndingNotificationConsumer.Handle(
                evt,
                Dispatcher.Dispatcher,
                Resolver,
                new OwnerEmail(),
                Scribe,
                new FakeTenantHostResolver(host),
                Options.Create(new PortalOptions()),
                FixedRecipientLocale.English,
                Correlation,
                NullLogger<SubscriptionDiscountEndingIntegrationEvent>.Instance,
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
