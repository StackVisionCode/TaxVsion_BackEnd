using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Campaigns.Application.Runs.Abstractions;
using TaxVision.Campaigns.Application.Scheduling.Abstractions;
using TaxVision.Campaigns.Domain.Runs;
using TaxVision.Campaigns.Domain.Scheduling;

namespace TaxVision.Campaigns.Application.Runs.Commands;

/// <summary>Agendado, slot y claim que disparan el run (Id determinista, ver <c>ScheduledRunId</c>).</summary>
public sealed record ScheduledSlot(Guid ScheduleId, DateTime SlotUtc, Guid LeaseToken);

/// <summary>
/// Paso 1 del patrón BB-8: el run queda confirmado en <c>Created</c> antes de llamar al Wallet, así la reserva lleva una
/// referencia que sobrevive a cualquier fallo posterior.
/// </summary>
public sealed record OpenCampaignRunCommand(
    Guid TenantId,
    Guid CampaignId,
    Guid TriggeredByUserId,
    string TriggerKind,
    IReadOnlyCollection<RunRecipientDraft> Units,
    ScheduledSlot? Slot = null
);

/// <summary><paramref name="AwaitsFunding"/> es falso si el run cerró al materializar (todo Skipped).</summary>
public sealed record OpenedCampaignRun(CampaignRunResponse Run, WalletUnitCounts Billable, bool AwaitsFunding)
{
    public static OpenedCampaignRun From(CampaignRun run) =>
        new(
            CampaignRunResponse.From(run),
            CampaignRunFunding.CountBillableUnits(run),
            run.Status == CampaignRunStatus.Created
        );
}

public static class OpenCampaignRunHandler
{
    public static async Task<Result<OpenedCampaignRun>> Handle(
        OpenCampaignRunCommand command,
        ICampaignRunRepository runs,
        ICampaignScheduleRepository schedules,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        CampaignSchedule? schedule = null;
        if (command.Slot is { } slot)
        {
            schedule = await schedules.GetByIdAsync(command.TenantId, slot.ScheduleId, ct);
            if (schedule is null || !schedule.HoldsLease(slot.LeaseToken, slot.SlotUtc))
                return Result.Failure<OpenedCampaignRun>(CampaignScheduleErrors.LeaseLost);
        }

        var opened = command.Slot is { } scheduled
            ? CampaignRun.StartScheduled(
                command.TenantId,
                command.CampaignId,
                command.TriggeredByUserId,
                scheduled.ScheduleId,
                scheduled.SlotUtc,
                command.Units
            )
            : CampaignRun.Start(
                command.TenantId,
                command.CampaignId,
                command.TriggeredByUserId,
                command.TriggerKind,
                command.Units
            );
        if (opened.IsFailure)
            return Result.Failure<OpenedCampaignRun>(opened.Error);

        var run = opened.Value;
        await runs.AddAsync(run, ct);

        // Sin nada que enviar no hay reserva: el run cerró y el agendado avanza en esta misma transacción.
        if (run.Status != CampaignRunStatus.Created)
        {
            run.AnnounceStart();
            schedule?.MarkFired(run.Id, DateTime.UtcNow, runIsTerminal: true);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(OpenedCampaignRun.From(run));
    }
}
