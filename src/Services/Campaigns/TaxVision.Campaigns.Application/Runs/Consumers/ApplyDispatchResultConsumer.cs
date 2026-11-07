using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Campaigns.Application.Runs.Abstractions;
using TaxVision.Campaigns.Domain.Runs;

namespace TaxVision.Campaigns.Application.Runs.Consumers;

/// <summary>
/// Consume <c>campaign.dispatch.result.v1</c> de cualquier ejecutor de canal y avanza la unidad
/// correspondiente (idempotente por <c>DispatchId</c>). Al cerrar el run por conteo sale
/// <c>campaign.run.completed.v1</c>. Tolerante a re-entrega (at-least-once).
/// </summary>
public static class ApplyDispatchResultConsumer
{
    public static async Task Handle(
        CampaignDispatchResultIntegrationEvent evt,
        ICampaignRunRepository runs,
        IUnitOfWork unitOfWork,
        ILogger<CampaignRun> logger,
        CancellationToken ct
    )
    {
        // Dato inválido del ejecutor: directo a la DLQ, donde se ve.
        if (!Enum.TryParse<DispatchOutcome>(evt.Outcome, ignoreCase: true, out var outcome))
            throw new ArgumentException($"Unknown dispatch outcome '{evt.Outcome}' for run {evt.RunId}.", nameof(evt));

        var run =
            await runs.GetForResultAsync(evt.TenantId, evt.RunId, evt.DispatchId, ct)
            ?? throw new DependencyNotReadyException("CampaignRun", $"run {evt.RunId}");

        var applied = run.ApplyResult(evt.DispatchId, outcome, evt.ProviderRef, evt.Reason);
        if (applied.IsFailure)
        {
            logger.LogWarning(
                "Dispatch result for unknown unit {DispatchId} in run {RunId}; ignoring.",
                evt.DispatchId,
                evt.RunId
            );
            CampaignsMetrics.RecordDispatchResultUnmatched();
            return;
        }

        await unitOfWork.SaveChangesAsync(ct);
    }
}
