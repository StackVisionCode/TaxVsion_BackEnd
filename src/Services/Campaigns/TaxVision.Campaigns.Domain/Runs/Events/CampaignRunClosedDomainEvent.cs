using BuildingBlocks.Domain;

namespace TaxVision.Campaigns.Domain.Runs.Events;

/// <summary>El run cerró. Los contadores son la foto del cierre.</summary>
public sealed record CampaignRunClosedDomainEvent(
    Guid RunId,
    Guid TenantId,
    Guid CampaignId,
    CampaignRunStatus TerminalStatus,
    int RecipientCount,
    int Delivered,
    int Accepted,
    int Failed,
    int Skipped,
    int Unknown
) : IDomainEvent;
