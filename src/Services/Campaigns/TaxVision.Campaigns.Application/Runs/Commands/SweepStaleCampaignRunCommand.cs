using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using TaxVision.Campaigns.Application.Runs.Abstractions;
using Wolverine;

namespace TaxVision.Campaigns.Application.Runs.Commands;

/// <summary>Cierra por tiempo un run que quedó en <c>Dispatching</c> (unidades sin resultado → <c>Unknown</c>).</summary>
public sealed record SweepStaleCampaignRunCommand(Guid TenantId, Guid RunId);

/// <summary>
/// Una unidad por transacción con outbox (CMP-03): si el run cierra, publica <c>run.completed</c> en la misma
/// transacción, y eso liquida el Wallet y libera el agendado. Idempotente: un run ya cerrado no cambia.
/// </summary>
public static class SweepStaleCampaignRunHandler
{
    public static async Task Handle(
        SweepStaleCampaignRunCommand command,
        ICampaignRunRepository runs,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var run = await runs.GetByIdAsync(command.TenantId, command.RunId, ct);
        if (run is null || !run.ExpireStaleUnits(DateTime.UtcNow))
            return;

        // Guardar antes de publicar: el sobre sale con el commit de la misma transacción.
        await unitOfWork.SaveChangesAsync(ct);
        await StartCampaignRunHandler.PublishCompletedAsync(bus, run, correlation.CorrelationId);
    }
}
