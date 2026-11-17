using System.Globalization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.GrowthIntegrationEvents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Preferences;

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
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribe,
        ITenantHostResolver hosts,
        IOptions<PortalOptions> portal,
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
            await SendEmailsAsync(evt, owners, amount, walletUrl, emails, gateway, scribe, correlation, ct);

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
                owners
            );
        }
    }

    private static async Task SendEmailsAsync(
        ReferralRewardGrantedIntegrationEvent evt,
        IReadOnlyList<Guid> owners,
        string? amount,
        string walletUrl,
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

        var render = (
            await scribe.RenderAsync(
                EmailEventKey,
                evt.TenantId,
                new Dictionary<string, object?>
                {
                    ["amount_label"] = amount ?? "Your reward",
                    ["wallet_url"] = walletUrl,
                },
                ct
            )
        ).EnsureRendered(EmailEventKey);

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
                    NotificationKind = NotificationKinds.ReferralRewardGranted,
                },
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
            var amount = ReferralRewardText.Amount(evt.RewardAmountCents, evt.RewardCurrency) ?? "Your referral reward";
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
                ct
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
        IReadOnlyList<Guid>? owners = null
    )
    {
        var recipients = owners ?? await OwnersAsync(resolver, tenantId, ct);
        if (recipients.Count == 0)
            return;

        await BillingNotificationPublisher.RecordAsync(
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
            new Dictionary<string, string> { ["rewardCaseId"] = rewardCaseId.ToString() },
            $"referrals:reward:{rewardCaseId}:{moment}",
            ct
        );
    }
}
