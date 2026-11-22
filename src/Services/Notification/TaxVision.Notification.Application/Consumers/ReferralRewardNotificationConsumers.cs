using System.Globalization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.GrowthIntegrationEvents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers;

/// <summary>El referido pagó su plan: el owner sabe cuánto gana y que llega al Wallet tras la espera.</summary>
public static class ReferralQualifiedNotificationConsumer
{
    public static async Task Handle(
        ReferralQualifiedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IRecipientResolver resolver,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var reward = ReferralRewardText.Amount(evt.RewardAmountCents, evt.RewardCurrency);
            await ReferralRewardText.RecordAsync(
                dispatcher,
                resolver,
                correlation,
                evt.TenantId,
                evt.EventId,
                evt.OccurredAt,
                NotificationKinds.ReferralQualified,
                evt.EventType,
                evt.AggregateId,
                "qualified",
                "Your referral paid their plan",
                reward is null
                    ? "Your reward reaches your Wallet after the waiting period."
                    : $"Your {reward} reward reaches your Wallet after the waiting period.",
                ct
            );
        }
    }
}

/// <summary>
/// El premio llegó al Wallet: correo y campana al owner. El correo se arma primero (lo único que puede fallar es Scribe),
/// así un reintento no duplica la campana. Las preferencias de correo las aplica el gateway.
/// </summary>
public static class ReferralRewardGrantedNotificationConsumer
{
    public const string EmailEventKey = "growth.referral_reward_granted.v1";

    public static async Task Handle(
        ReferralRewardGrantedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IRecipientResolver resolver,
        IUserEmailResolver emails,
        IScribeRenderClient scribe,
        ITenantHostResolver hosts,
        IOptions<PortalOptions> portal,
        IRecipientLocale locales,
        ICorrelationContext correlation,
        ILogger<ReferralRewardGrantedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var owners = await ReferralRewardText.OwnersAsync(resolver, evt.TenantId, ct);
            if (owners.Count == 0)
            {
                logger.LogWarning("Referral reward {RewardCaseId} has no office owner to notify.", evt.RewardCaseId);
                return;
            }

            var amount = ReferralRewardText.Amount(evt.RewardAmountCents, evt.RewardCurrency);
            var host = await hosts.ResolveHostAsync(evt.TenantId, ct);
            var walletUrl = $"{TenantEmailLinks.StaffBase(host, portal.Value)}/wallet";
            await SendEmailsAsync(evt, owners, amount, walletUrl, emails, dispatcher, scribe, locales, correlation, ct);

            await ReferralRewardText.RecordAsync(
                dispatcher,
                resolver,
                correlation,
                evt.TenantId,
                evt.EventId,
                evt.OccurredAt,
                NotificationKinds.ReferralRewardGranted,
                evt.EventType,
                evt.RewardCaseId,
                "granted",
                "Your referral reward is in your Wallet",
                amount is null
                    ? "Your referral reward was added to your Wallet."
                    : $"{amount} was added to your Wallet.",
                ct,
                owners,
                amount
            );
        }
    }

    private static async Task SendEmailsAsync(
        ReferralRewardGrantedIntegrationEvent evt,
        IReadOnlyList<Guid> owners,
        string? amount,
        string walletUrl,
        IUserEmailResolver emails,
        NotificationDispatcher dispatcher,
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
        var renders = new RecipientRenders(
            scribe,
            EmailEventKey,
            evt.TenantId,
            ScribeRenderAudience.Staff,
            locale => new Dictionary<string, object?>
            {
                ["amount_label"] = amount ?? EmailText.For("referral.reward_fallback", locale),
                ["wallet_url"] = walletUrl,
            }
        );
        var languages = new RecipientLocaleCache(locales, evt.TenantId);
        foreach (var (userId, _) in addresses)
            await renders.ForAsync(await languages.ForUserAsync(userId, ct), ct);

        foreach (var (userId, address) in addresses)
            await dispatcher.DispatchAsync(
                new NotificationIntent(
                    NotificationKinds.ReferralRewardGranted,
                    evt.TenantId,
                    new NotificationTarget(userId, address),
                    evt.EventId,
                    evt.EventType,
                    evt.OccurredAt,
                    correlation.CorrelationId,
                    Email: new EmailContent(
                        EmailEventKey,
                        new Dictionary<string, object?>(),
                        ScribeRenderAudience.Staff,
                        Render: renders.ForAsync
                    )
                ),
                ct
            );
    }
}

/// <summary>El premio se canceló antes de acreditarse: el owner sabe por qué no se pagará.</summary>
public static class ReferralRewardRejectedNotificationConsumer
{
    public static async Task Handle(
        ReferralRewardRejectedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IRecipientResolver resolver,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var why = evt.ReasonCode switch
            {
                "ReferredPaymentRefunded" => "The referred office's payment was refunded",
                "ReferredPaymentDisputeLost" => "The referred office's payment was disputed",
                _ => "This reward was reviewed",
            };
            await ReferralRewardText.RecordAsync(
                dispatcher,
                resolver,
                correlation,
                evt.TenantId,
                evt.EventId,
                evt.OccurredAt,
                NotificationKinds.ReferralRewardRejected,
                evt.EventType,
                evt.RewardCaseId,
                "rejected",
                "A referral reward was cancelled",
                $"{why}, so this reward won't be paid.",
                ct
            );
        }
    }
}

/// <summary>El premio ya acreditado se quitó del Wallet: el owner sabe cuánto y por qué.</summary>
public static class ReferralRewardReversedNotificationConsumer
{
    public static async Task Handle(
        ReferralRewardReversedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IRecipientResolver resolver,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var knownAmount = ReferralRewardText.Amount(evt.RewardAmountCents, evt.RewardCurrency);
            var amount = knownAmount ?? "Your referral reward";
            await ReferralRewardText.RecordAsync(
                dispatcher,
                resolver,
                correlation,
                evt.TenantId,
                evt.EventId,
                evt.OccurredAt,
                NotificationKinds.ReferralRewardReversed,
                evt.EventType,
                evt.RewardCaseId,
                "reversed",
                "A referral reward was reversed",
                $"{amount} was removed from your Wallet because the referred office's payment was refunded or disputed.",
                ct,
                amount: knownAmount
            );
        }
    }
}

/// <summary>Texto y campana comunes de los avisos del premio: monto en inglés y agrupación por premio y momento.</summary>
internal static class ReferralRewardText
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    public static string? Amount(long? cents, string? currency)
    {
        if (cents is not { } value || string.IsNullOrWhiteSpace(currency))
            return null;

        var amount = value / 100m;
        return string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase)
            ? amount.ToString("C", English)
            : $"{amount.ToString("N2", English)} {currency.ToUpperInvariant()}";
    }

    // El monto viaja solo si el evento lo trae: sin él, el frontend usa el texto del servidor.
    private static Dictionary<string, string> Parameters(Guid rewardCaseId, string? amount)
    {
        var parameters = new Dictionary<string, string> { ["rewardCaseId"] = rewardCaseId.ToString() };
        if (amount is not null)
            parameters["amount"] = amount;
        return parameters;
    }

    public static async Task<IReadOnlyList<Guid>> OwnersAsync(
        IRecipientResolver resolver,
        Guid tenantId,
        CancellationToken ct
    )
    {
        var owner = await resolver.ResolveAsync(new TenantOwnerAudience(tenantId), RecipientValidation.None, ct);
        return owner.IsSuccess ? owner.Value : [];
    }

    public static async Task RecordAsync(
        NotificationDispatcher dispatcher,
        IRecipientResolver resolver,
        ICorrelationContext correlation,
        Guid tenantId,
        Guid eventId,
        DateTime occurredOn,
        string kind,
        string sourceEventType,
        Guid rewardCaseId,
        string moment,
        string title,
        string body,
        CancellationToken ct,
        IReadOnlyList<Guid>? owners = null,
        string? amount = null
    )
    {
        var recipients = owners ?? await OwnersAsync(resolver, tenantId, ct);
        if (recipients.Count == 0)
            return;

        await NotifyAsync(
            dispatcher,
            recipients,
            tenantId,
            eventId,
            correlation.CorrelationId,
            kind,
            sourceEventType,
            occurredOn,
            title,
            body,
            rewardCaseId.ToString(),
            Parameters(rewardCaseId, amount),
            $"referrals:reward:{rewardCaseId}:{moment}",
            ct
        );
    }

    /// <summary>In-app a cada dueño, con el texto del servidor como respaldo.</summary>
    public static async Task NotifyAsync(
        NotificationDispatcher dispatcher,
        IReadOnlyList<Guid> recipients,
        Guid tenantId,
        Guid eventId,
        string correlationId,
        string kind,
        string sourceEventType,
        DateTime occurredOn,
        string title,
        string body,
        string resourceId,
        IReadOnlyDictionary<string, string> parameters,
        string groupKey,
        CancellationToken ct
    )
    {
        foreach (var userId in recipients)
            await dispatcher.DispatchAsync(
                new NotificationIntent(
                    kind,
                    tenantId,
                    new NotificationTarget(userId),
                    eventId,
                    sourceEventType,
                    occurredOn,
                    correlationId,
                    InApp: new InAppContent(
                        Params: parameters,
                        ResourceId: resourceId,
                        GroupKey: groupKey,
                        FallbackTitle: title,
                        FallbackBody: body
                    )
                ),
                ct
            );
    }
}
