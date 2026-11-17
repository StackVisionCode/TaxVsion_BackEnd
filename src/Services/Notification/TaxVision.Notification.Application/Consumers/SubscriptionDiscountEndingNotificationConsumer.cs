using System.Globalization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers;

/// <summary>
/// Un descuento de N meses termina en 7 días: correo y campana al owner. El correo se arma primero (lo único que puede
/// fallar es Scribe), así un reintento no duplica la campana; el id del evento deduplica el correo.
/// </summary>
public static class SubscriptionDiscountEndingNotificationConsumer
{
    public const string EmailEventKey = "subscription.discount_ending.v1";

    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    public static async Task Handle(
        SubscriptionDiscountEndingIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IRecipientResolver resolver,
        IUserEmailResolver emails,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribe,
        ITenantHostResolver hosts,
        IOptions<PortalOptions> portal,
        ICorrelationContext correlation,
        ILogger<SubscriptionDiscountEndingIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var owner = await resolver.ResolveAsync(
                new TenantOwnerAudience(evt.TenantId),
                RecipientValidation.None,
                ct
            );
            var owners = owner.IsSuccess ? owner.Value : [];
            if (owners.Count == 0)
            {
                logger.LogWarning("Discount {DiscountId} has no office owner to notify.", evt.DiscountId);
                return;
            }

            var label = Label(evt);
            var host = await hosts.ResolveHostAsync(evt.TenantId, ct);
            var variables = new Dictionary<string, object?>
            {
                ["discount_label"] = label,
                ["days_left"] = evt.DaysUntilEnd.ToString(CultureInfo.InvariantCulture),
                ["billing_url"] = TenantEmailLinks.StaffBase(host, portal.Value),
            };
            await SendEmailsAsync(evt, owners, variables, emails, gateway, scribe, correlation, ct);

            await BillingNotificationPublisher.RecordAsync(
                dispatcher,
                owners,
                evt.TenantId,
                evt.EventId,
                correlation.CorrelationId,
                NotificationKinds.SubscriptionDiscountEnding,
                nameof(SubscriptionDiscountEndingIntegrationEvent),
                evt.OccurredOn,
                "Your discount ends soon",
                $"Your {label} discount ends in {evt.DaysUntilEnd} days. After that, your plan renews at its regular price.",
                evt.DiscountId.ToString(),
                new Dictionary<string, string> { ["discountId"] = evt.DiscountId.ToString() },
                $"subscription:discount:{evt.DiscountId}:ending",
                ct
            );
        }
    }

    // "20%" o "$5.00": el beneficio tal como lo ve el owner.
    private static string Label(SubscriptionDiscountEndingIntegrationEvent evt)
    {
        if (evt.PercentBasisPoints is { } basisPoints)
            return $"{(basisPoints / 100m).ToString("0.##", English)}%";

        var amount = (evt.AmountCents ?? 0) / 100m;
        return evt.Currency is null || string.Equals(evt.Currency, "USD", StringComparison.OrdinalIgnoreCase)
            ? amount.ToString("C", English)
            : $"{amount.ToString("N2", English)} {evt.Currency.ToUpperInvariant()}";
    }

    private static async Task SendEmailsAsync(
        SubscriptionDiscountEndingIntegrationEvent evt,
        IReadOnlyList<Guid> owners,
        Dictionary<string, object?> variables,
        IUserEmailResolver emails,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribe,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var addresses = new List<(Guid UserId, string Address)>();
        foreach (var userId in owners)
        {
            var address = await emails.ResolveAsync(evt.TenantId, userId, ct);
            if (
                !string.IsNullOrWhiteSpace(address)
                && !addresses.Exists(a => string.Equals(a.Address, address, StringComparison.OrdinalIgnoreCase))
            )
                addresses.Add((userId, address));
        }
        if (addresses.Count == 0)
            return;

        var render = (await scribe.RenderAsync(EmailEventKey, evt.TenantId, variables, ct)).EnsureRendered(
            EmailEventKey
        );
        foreach (var (userId, address) in addresses)
            await gateway.QueueEmailAsync(
                render.ToDispatchRequest(
                    evt.TenantId,
                    address,
                    EmailEventKey,
                    evt.EventId,
                    correlation.CorrelationId
                ) with
                {
                    RecipientUserId = userId,
                    Category = NotificationCategory.Billing,
                    NotificationKind = NotificationKinds.SubscriptionDiscountEnding,
                },
                ct
            );
    }
}
