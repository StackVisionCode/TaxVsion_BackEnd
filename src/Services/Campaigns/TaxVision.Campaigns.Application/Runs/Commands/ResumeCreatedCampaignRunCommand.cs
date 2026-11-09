using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Campaigns.Application.Runs.Abstractions;
using TaxVision.Campaigns.Application.Scheduling;
using TaxVision.Campaigns.Application.Scheduling.Abstractions;
using TaxVision.Campaigns.Domain.Runs;
using TaxVision.Campaigns.Domain.Scheduling;
using Wolverine;
using Wolverine.Attributes;

namespace TaxVision.Campaigns.Application.Runs.Commands;

/// <summary>Retoma un run que quedó en <c>Created</c> (paso 4 del patrón BB-8).</summary>
public sealed record ResumeCreatedCampaignRunCommand(Guid TenantId, Guid RunId);

/// <summary>
/// Repite la reserva con la misma referencia y sigue al paso 3. Un agendado vivo no se toca: lo retoma su próximo claim.
/// Uno cerrado ya no va a enviar, así que su run se rechaza y lo apartado se libera.
/// </summary>
public static class ResumeCreatedCampaignRunHandler
{
    [NonTransactional]
    public static async Task Handle(
        ResumeCreatedCampaignRunCommand command,
        ICampaignRunRepository runs,
        ICampaignScheduleRepository schedules,
        IOptions<CampaignSchedulerOptions> schedulerOptions,
        IWalletSpendClient wallet,
        IMessageBus bus,
        ILogger<CampaignRun> logger,
        CancellationToken ct
    )
    {
        var run = await runs.GetByIdAsync(command.TenantId, command.RunId, ct);
        if (run is null || run.Status != CampaignRunStatus.Created)
            return;

        if (run.ScheduleId is { } scheduleId)
        {
            var schedule = await schedules.GetByIdAsync(command.TenantId, scheduleId, ct);
            if (schedule is { IsOpen: true } && schedule.NextFireAtUtc == run.ScheduledSlotUtc)
                return;

            await ApplyAsync(bus, run, RunFunding.WalletUnavailable, ct);
            return;
        }

        var funding = await CampaignRunFunding.ReserveAsync(
            wallet,
            run.TenantId,
            run.Id,
            CampaignRunFunding.CountBillableUnits(run),
            callerBearerToken: null,
            ct
        );

        // El Wallet sigue caído: el run espera a la próxima vuelta, hasta el mismo plazo que un agendado.
        if (
            funding.Outcome == RunFundingOutcome.WalletUnavailable
            && DateTime.UtcNow - run.CreatedAtUtc <= schedulerOptions.Value.MaxFireDelay
        )
        {
            logger.LogWarning("Run {RunId} still waits for the Wallet; next sweep retries.", run.Id);
            return;
        }

        await ApplyAsync(bus, run, funding, ct);
    }

    private static async Task ApplyAsync(IMessageBus bus, CampaignRun run, RunFunding funding, CancellationToken ct) =>
        await bus.InvokeAsync<Result<CampaignRunResponse>>(
            new ApplyCampaignRunFundingCommand(run.TenantId, run.Id, funding, Slot: null, run.Id.ToString("N")),
            ct
        );
}
