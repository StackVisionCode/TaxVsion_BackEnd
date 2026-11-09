using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using TaxVision.Campaigns.Domain.Runs.Events;
using Wolverine;

namespace TaxVision.Campaigns.Application.Runs.DomainEvents;

/// <summary>
/// Anuncia el cierre del run: lo consumen la liquidación del Wallet y la liberación del agendado. Todo lo que
/// lleva el aviso viene en el evento; no relee el run.
/// </summary>
public static class CampaignRunClosedDomainEventHandler
{
    public static CampaignRunCompletedIntegrationEvent Handle(CampaignRunClosedDomainEvent closed, Envelope envelope) =>
        new()
        {
            TenantId = closed.TenantId,
            CorrelationId = envelope.CorrelationId ?? string.Empty,
            CampaignId = closed.CampaignId,
            RunId = closed.RunId,
            TerminalStatus = closed.TerminalStatus.ToString(),
            RecipientCount = closed.RecipientCount,
            Delivered = closed.Delivered,
            Accepted = closed.Accepted,
            Failed = closed.Failed,
            Skipped = closed.Skipped,
            Unknown = closed.Unknown,
        };
}
