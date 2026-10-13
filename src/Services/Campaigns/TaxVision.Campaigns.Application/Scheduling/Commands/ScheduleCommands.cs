using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Campaigns.Application.Campaigns.Abstractions;
using TaxVision.Campaigns.Application.Scheduling.Abstractions;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Scheduling;

namespace TaxVision.Campaigns.Application.Scheduling.Commands;

// ─────────────────────────── Create schedule ───────────────────────────

/// <summary>
/// Agenda una campaña: <c>Recurring=false</c> → una vez en <paramref name="RunAtUtc"/>; <c>Recurring=true</c>
/// → primer disparo en <paramref name="RunAtUtc"/> y luego cada <paramref name="IntervalMinutes"/>. La
/// audiencia se resuelve en cada disparo desde <paramref name="ContactListIds"/>.
/// </summary>
public sealed record ScheduleCampaignCommand(
    Guid TenantId,
    Guid CampaignId,
    bool Recurring,
    DateTime RunAtUtc,
    int? IntervalMinutes,
    IReadOnlyList<Guid> ContactListIds,
    bool IncludeCustomers = false,
    // A1 — quién agenda y qué clientes ve. El disparo (actor de sistema) hereda esto en vez de correr con
    // la visibilidad abierta. Ver CampaignSchedule.CreatorCanViewAllCustomers.
    Guid? CreatedByUserId = null,
    bool CreatorCanViewAllCustomers = true,
    // Clientes seleccionados (snapshot al agendar). No vacío ⇒ solo esos en cada disparo.
    IReadOnlyList<Guid>? CustomerIds = null,
    // Recurrencia: frecuencia nombrada (Hourly/Daily/Weekly/Monthly) o Custom (usa IntervalMinutes).
    // Default Custom para compatibilidad con llamadas que solo mandan IntervalMinutes.
    RecurrenceFrequency Frequency = RecurrenceFrequency.Custom,
    // Fin opcional por lo que ocurra primero: fecha o cantidad de disparos.
    DateTime? EndsAtUtc = null,
    int? MaxOccurrences = null
);

public static class ScheduleCampaignHandler
{
    public static async Task<Result<CampaignScheduleResponse>> Handle(
        ScheduleCampaignCommand command,
        ICampaignRepository campaigns,
        ICampaignScheduleRepository schedules,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var campaign = await campaigns.GetByIdAsync(command.TenantId, command.CampaignId, ct);
        if (campaign is null)
            return Result.Failure<CampaignScheduleResponse>(CampaignErrors.NotFound);
        if (campaign.Status == CampaignStatus.Archived)
            return Result.Failure<CampaignScheduleResponse>(CampaignErrors.Archived);

        var lists = command.ContactListIds ?? [];
        var result = command.Recurring
            ? CampaignSchedule.CreateRecurring(
                command.TenantId,
                command.CampaignId,
                command.RunAtUtc,
                command.Frequency,
                command.IntervalMinutes,
                lists,
                command.IncludeCustomers,
                command.CreatedByUserId,
                command.CreatorCanViewAllCustomers,
                command.CustomerIds,
                command.EndsAtUtc,
                command.MaxOccurrences
            )
            : CampaignSchedule.CreateOneTime(
                command.TenantId,
                command.CampaignId,
                command.RunAtUtc,
                lists,
                command.IncludeCustomers,
                command.CreatedByUserId,
                command.CreatorCanViewAllCustomers,
                command.CustomerIds
            );
        if (result.IsFailure)
            return Result.Failure<CampaignScheduleResponse>(result.Error);

        await schedules.AddAsync(result.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(CampaignScheduleResponse.From(result.Value));
    }
}

// ─────────────────────────── Pause / Resume / Cancel ───────────────────────────

public sealed record SetScheduleStateCommand(Guid TenantId, Guid ScheduleId, ScheduleAction Action);

public enum ScheduleAction
{
    Pause,
    Resume,
    Cancel,
}

public static class SetScheduleStateHandler
{
    public static async Task<Result<CampaignScheduleResponse>> Handle(
        SetScheduleStateCommand command,
        ICampaignScheduleRepository schedules,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var schedule = await schedules.GetByIdAsync(command.TenantId, command.ScheduleId, ct);
        if (schedule is null)
            return Result.Failure<CampaignScheduleResponse>(CampaignScheduleErrors.NotFound);

        var action = command.Action switch
        {
            ScheduleAction.Pause => schedule.Pause(),
            ScheduleAction.Resume => schedule.Resume(),
            ScheduleAction.Cancel => schedule.Cancel(),
            _ => Result.Failure(CampaignScheduleErrors.InvalidTransition),
        };
        if (action.IsFailure)
            return Result.Failure<CampaignScheduleResponse>(action.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(CampaignScheduleResponse.From(schedule));
    }
}
