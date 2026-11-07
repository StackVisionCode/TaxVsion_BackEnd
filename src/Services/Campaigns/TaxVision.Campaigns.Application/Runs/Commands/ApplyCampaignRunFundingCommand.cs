using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Campaigns.Application.Runs.Abstractions;
using TaxVision.Campaigns.Application.Scheduling.Abstractions;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Runs;
using TaxVision.Campaigns.Domain.Scheduling;
using Wolverine;

namespace TaxVision.Campaigns.Application.Runs.Commands;

/// <summary>
/// Paso 3 del patrón BB-8: aplica lo que respondió el Wallet en su propia transacción. Con fondos, el run despacha; sin
/// fondos se rechaza. Si el run ya no está en <c>Created</c>, otro camino lo resolvió y no hace nada. En un agendado, el
/// avance se confirma junto con el run (CMP-01). <paramref name="Funding"/> en <c>WalletUnavailable</c> llega solo
/// cuando ya no se va a esperar más al Wallet.
/// </summary>
public sealed record ApplyCampaignRunFundingCommand(
    Guid TenantId,
    Guid RunId,
    RunFunding Funding,
    ScheduledSlot? Slot,
    string? CorrelationId
);

public static class ApplyCampaignRunFundingHandler
{
    public static async Task<Result<CampaignRunResponse>> Handle(
        ApplyCampaignRunFundingCommand command,
        ICampaignRunRepository runs,
        CampaignRunDispatcher dispatcher,
        ICampaignScheduleRepository schedules,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ILogger<CampaignRun> logger,
        CancellationToken ct
    )
    {
        var run = await runs.GetByIdAsync(command.TenantId, command.RunId, ct);
        if (run is null)
            return Result.Failure<CampaignRunResponse>(CampaignRunErrors.NotFound);
        if (run.Status != CampaignRunStatus.Created)
            return Result.Success(CampaignRunResponse.From(run));

        CampaignSchedule? schedule = null;
        if (command.Slot is { } slot)
        {
            schedule = await schedules.GetByIdAsync(command.TenantId, slot.ScheduleId, ct);
            if (schedule is null || !schedule.HoldsLease(slot.LeaseToken, slot.SlotUtc))
                return Result.Failure<CampaignRunResponse>(CampaignScheduleErrors.LeaseLost);
        }

        var nowUtc = DateTime.UtcNow;
        var campaign = await dispatcher.GetCampaignAsync(command.TenantId, run.CampaignId, ct);
        if (campaign is null or { Status: CampaignStatus.Archived })
        {
            run.Reject("campaign_archived");
            schedule?.MarkFired(Guid.Empty, nowUtc);
            await RejectAsync(unitOfWork, bus, run, releaseReservation: true, ct);
            return Result.Failure<CampaignRunResponse>(
                campaign is null ? CampaignErrors.NotFound : CampaignErrors.Archived
            );
        }

        switch (command.Funding.Outcome)
        {
            case RunFundingOutcome.Reserved:
                var dispatches = await dispatcher.StartDispatchingAsync(run, campaign, command.CorrelationId, ct);
                schedule?.MarkFired(run.Id, nowUtc, runIsTerminal: false);

                // Guardar antes de publicar: el reintento en línea suelta lo publicado en el intento revertido.
                await unitOfWork.SaveChangesAsync(ct);
                foreach (var dispatch in dispatches)
                    await bus.PublishAsync(dispatch);
                return Result.Success(CampaignRunResponse.From(run));

            case RunFundingOutcome.InsufficientFunds:
                run.Reject($"insufficient_funds:{command.Funding.DeficitMicros}");
                schedule?.MarkFired(Guid.Empty, nowUtc);
                await RejectAsync(unitOfWork, bus, run, releaseReservation: false, ct);
                return Result.Failure<CampaignRunResponse>(
                    CampaignRunErrors.InsufficientFunds(command.Funding.DeficitMicros, command.Funding.Currency)
                );

            default:
                // Un OneTime no se da por enviado: queda pausado para la oficina. El run sigue en Created y, al
                // reanudar, el mismo slot retoma la misma referencia.
                if (schedule is { Kind: ScheduleKind.OneTime })
                {
                    schedule.Pause(SchedulePauseReason.WalletUnavailable);
                    await unitOfWork.SaveChangesAsync(ct);
                    logger.LogError(
                        "Schedule {ScheduleId} paused: the Wallet stayed unavailable past the firing window.",
                        schedule.Id
                    );
                    return Result.Failure<CampaignRunResponse>(CampaignRunErrors.WalletUnavailable);
                }

                run.Reject("wallet_unavailable");
                schedule?.MarkFired(Guid.Empty, nowUtc);
                await RejectAsync(unitOfWork, bus, run, releaseReservation: true, ct);
                return Result.Failure<CampaignRunResponse>(CampaignRunErrors.WalletUnavailable);
        }
    }

    // Una reserva pudo quedar del lado del Wallet (respuesta perdida): se libera sin depender de esta petición.
    private static async Task RejectAsync(
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CampaignRun run,
        bool releaseReservation,
        CancellationToken ct
    )
    {
        await unitOfWork.SaveChangesAsync(ct);
        if (releaseReservation)
            await bus.PublishAsync(new ReleaseCampaignRunReservationCommand(run.TenantId, run.Id));
    }
}
