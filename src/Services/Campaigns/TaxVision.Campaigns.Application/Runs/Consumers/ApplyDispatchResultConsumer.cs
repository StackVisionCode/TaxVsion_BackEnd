using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Campaigns.Application.Runs.Abstractions;
using TaxVision.Campaigns.Application.Runs.Commands;
using TaxVision.Campaigns.Domain.Runs;
using Wolverine;

namespace TaxVision.Campaigns.Application.Runs.Consumers;

/// <summary>
/// Consume <c>campaign.dispatch.result.v1</c> de cualquier ejecutor de canal y avanza la unidad
/// correspondiente (idempotente por <c>DispatchId</c>). Al cerrar el run por conteo publica
/// <c>campaign.run.completed.v1</c>. Tolerante a re-entrega (at-least-once).
/// </summary>
public static class ApplyDispatchResultConsumer
{
    public static async Task Handle(
        CampaignDispatchResultIntegrationEvent evt,
        ICampaignRunRepository runs,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<CampaignRun> logger,
        CancellationToken ct
    )
    {
        if (!Enum.TryParse<DispatchOutcome>(evt.Outcome, ignoreCase: true, out var outcome))
        {
            logger.LogWarning(
                "Unknown dispatch outcome '{Outcome}' for run {RunId}; ignoring.",
                evt.Outcome,
                evt.RunId
            );
            return;
        }

        var run = await runs.GetByIdAsync(evt.TenantId, evt.RunId, ct);
        if (run is null)
        {
            logger.LogWarning(
                "Dispatch result for unknown run {RunId} (tenant {TenantId}); ignoring.",
                evt.RunId,
                evt.TenantId
            );
            return;
        }

        var applied = run.ApplyResult(evt.DispatchId, outcome, evt.ProviderRef, evt.Reason);
        if (applied.IsFailure)
        {
            logger.LogWarning(
                "Dispatch result for unknown unit {DispatchId} in run {RunId}; ignoring.",
                evt.DispatchId,
                evt.RunId
            );
            return;
        }

        if (applied.Value)
            await StartCampaignRunHandler.PublishCompletedAsync(bus, run, correlation.CorrelationId);

        await unitOfWork.SaveChangesAsync(ct);
    }
}
