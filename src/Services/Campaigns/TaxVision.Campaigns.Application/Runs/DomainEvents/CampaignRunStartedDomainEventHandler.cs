using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using TaxVision.Campaigns.Domain.Runs.Events;
using Wolverine;

namespace TaxVision.Campaigns.Application.Runs.DomainEvents;

/// <summary>Anuncia el arranque del run. Corre después del commit, desde la cola domain-events.</summary>
public static class CampaignRunStartedDomainEventHandler
{
    public static CampaignRunStartedIntegrationEvent Handle(CampaignRunStartedDomainEvent started, Envelope envelope) =>
        new()
        {
            TenantId = started.TenantId,
            CorrelationId = envelope.CorrelationId ?? string.Empty,
            CampaignId = started.CampaignId,
            RunId = started.RunId,
            RecipientCount = started.RecipientCount,
            TriggeredBy = started.TriggerKind,
        };
}
