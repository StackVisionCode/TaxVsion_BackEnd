using BuildingBlocks.Common;
using BuildingBlocks.Messaging.GrowthIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers;

/// <summary>
/// Alguien se registró con el código de referido de la oficina: se le avisa al OWNER en la app. El aviso no lleva datos
/// del referido (aún no tiene oficina); el premio llega al Wallet cuando pague su plan y venza la espera.
/// </summary>
public static class ReferralAttributedNotificationConsumer
{
    public static async Task Handle(
        ReferralAttributedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IRecipientResolver resolver,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(string.Empty, evt.EventId)))
        {
            var owner = await resolver.ResolveAsync(
                new TenantOwnerAudience(evt.TenantId),
                RecipientValidation.None,
                ct
            );
            IReadOnlyList<Guid> recipients = owner.IsSuccess ? owner.Value : [];
            if (recipients.Count == 0)
                return;

            var via = string.IsNullOrWhiteSpace(evt.Channel) ? string.Empty : $" via {evt.Channel}";
            await BillingNotificationPublisher.RecordAsync(
                dispatcher,
                recipients,
                evt.TenantId,
                evt.EventId,
                correlation.CorrelationId,
                NotificationKinds.ReferralAttributed,
                "growth.referrals.attributed.v1",
                evt.OccurredAt,
                "Someone signed up with your referral code",
                $"A new office registered{via} with your code. You earn your reward in the Wallet when they pay their plan.",
                evt.AttributionId.ToString(),
                new Dictionary<string, string>
                {
                    ["attributionId"] = evt.AttributionId.ToString(),
                    ["channel"] = evt.Channel ?? string.Empty,
                },
                $"referrals:attributed:{evt.AttributionId}",
                ct
            );
        }
    }
}
