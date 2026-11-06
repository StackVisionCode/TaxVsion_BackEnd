using BuildingBlocks.Domain;

namespace TaxVision.Campaigns.Domain.Runs.Events;

/// <summary>Arrancó el envío: el Wallet ya autorizó y sale el fan-out.</summary>
public sealed record CampaignRunStartedDomainEvent(
    Guid RunId,
    Guid TenantId,
    Guid CampaignId,
    int RecipientCount,
    string TriggerKind
) : IDomainEvent;
