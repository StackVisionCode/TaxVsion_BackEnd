using System.Globalization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

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
        IScribeRenderClient scribe,
        ITenantHostResolver hosts,
        IOptions<PortalOptions> portal,
        IRecipientLocale locales,
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
            await SendEmailsAsync(evt, owners, variables, dispatcher, emails, scribe, locales, correlation, ct);

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
                $"Your {label} discount ends soon. Days left: {evt.DaysUntilEnd}. After that, your plan renews at its regular price.",
                evt.DiscountId.ToString(),
                new Dictionary<string, string>
                {
                    ["discountId"] = evt.DiscountId.ToString(),
                    ["discountLabel"] = label,
                    ["daysLeft"] = evt.DaysUntilEnd.ToString(CultureInfo.InvariantCulture),
                },
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
        NotificationDispatcher dispatcher,
        IUserEmailResolver emails,
        IScribeRenderClient scribe,
        IRecipientLocale locales,
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

        // Un render por idioma y lector; todos antes de encolar.
        var renders = new RecipientRenders(scribe, EmailEventKey, evt.TenantId, ScribeRenderAudience.Staff, variables);
        var languages = new RecipientLocaleCache(locales, evt.TenantId);
        var targets = new List<NotificationTarget>(addresses.Count);
        foreach (var (userId, address) in addresses)
        {
            var locale = await languages.ForUserAsync(userId, ct);
            await renders.ForAsync(locale, ct);
            targets.Add(new NotificationTarget(userId, address, Locale: locale));
        }

        foreach (var target in targets)
            await dispatcher.DispatchAsync(
                new NotificationIntent(
                    NotificationKinds.SubscriptionDiscountEnding,
                    evt.TenantId,
                    target,
                    evt.EventId,
                    nameof(SubscriptionDiscountEndingIntegrationEvent),
                    evt.OccurredOn,
                    correlation.CorrelationId,
                    Email: new EmailContent(EmailEventKey, variables, Render: renders.ForAsync)
                ),
                ct
            );
    }
}
