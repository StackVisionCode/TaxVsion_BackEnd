using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Campaigns.Application.Campaigns.Abstractions;
using TaxVision.Campaigns.Application.Contacts.Abstractions;
using TaxVision.Campaigns.Application.Runs;
using TaxVision.Campaigns.Application.Runs.Abstractions;
using TaxVision.Campaigns.Application.Runs.Audience;
using TaxVision.Campaigns.Application.Runs.Commands;
using TaxVision.Campaigns.Application.Scheduling.Abstractions;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Runs;
using TaxVision.Campaigns.Domain.Scheduling;
using Wolverine;
using Wolverine.Attributes;

namespace TaxVision.Campaigns.Application.Scheduling.Commands;

/// <summary>Dispara el slot <paramref name="SlotUtc"/> de un agendado reclamado con <paramref name="LeaseToken"/>.</summary>
public sealed record FireCampaignScheduleCommand(Guid TenantId, Guid ScheduleId, Guid LeaseToken, DateTime SlotUtc);

/// <summary>Salta el slot sin run: regla de negocio o audiencia que no respondió a tiempo. Avanza el agendado.</summary>
public sealed record SkipScheduledSlotCommand(Guid TenantId, Guid ScheduleId, Guid LeaseToken, DateTime SlotUtc);

public static class SkipScheduledSlotHandler
{
    public static async Task<Result> Handle(
        SkipScheduledSlotCommand command,
        ICampaignScheduleRepository schedules,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var schedule = await schedules.GetByIdAsync(command.TenantId, command.ScheduleId, ct);
        if (schedule is null || !schedule.HoldsLease(command.LeaseToken, command.SlotUtc))
            return Result.Failure(CampaignScheduleErrors.LeaseLost);

        schedule.MarkFired(Guid.Empty, DateTime.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

/// <summary>
/// Orquestador sin transacción (patrón BB-8). El run del slot se confirma en <c>Created</c>, el Wallet reserva sin
/// transacción abierta y el resultado se aplica en otra unidad, donde run, unidades, avance del agendado y sobres se
/// confirman juntos (CMP-01). El Id del run es determinista por slot: otro claim retoma el mismo run y la misma reserva.
/// </summary>
public static class FireCampaignScheduleHandler
{
    // Actor de sistema cuando el agendado no tiene creador.
    private static readonly Guid SchedulerActor = new("5c4ed011-0000-0000-0000-0000005c4ed0");

    [NonTransactional]
    public static async Task<Result<CampaignRunResponse>> Handle(
        FireCampaignScheduleCommand command,
        ICampaignScheduleRepository schedules,
        ICampaignRepository campaigns,
        ICampaignRunRepository runs,
        IContactRepository contacts,
        IContactListRepository lists,
        ICustomerAudienceClient customerClient,
        ICampaignCustomerAssignmentReader assignmentReader,
        IOptions<CampaignsVisibilityOptions> visibility,
        IOptions<CampaignSchedulerOptions> schedulerOptions,
        IWalletSpendClient wallet,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<CampaignSchedule> logger,
        CancellationToken ct
    )
    {
        var schedule = await schedules.GetByIdAsync(command.TenantId, command.ScheduleId, ct);
        if (schedule is null || !schedule.HoldsLease(command.LeaseToken, command.SlotUtc))
        {
            logger.LogInformation(
                "Schedule {ScheduleId} slot {Slot:o} is no longer held by this claim; skipping.",
                command.ScheduleId,
                command.SlotUtc
            );
            return Result.Failure<CampaignRunResponse>(CampaignScheduleErrors.LeaseLost);
        }

        var withinMaxDelay = DateTime.UtcNow - command.SlotUtc <= schedulerOptions.Value.MaxFireDelay;
        var slot = new ScheduledSlot(schedule.Id, command.SlotUtc, command.LeaseToken);
        var opened = await OpenRunAsync(
            schedule,
            slot,
            campaigns,
            runs,
            contacts,
            lists,
            customerClient,
            assignmentReader,
            visibility.Value,
            bus,
            ct
        );

        if (opened.IsFailure && opened.Error == CampaignRunErrors.AudienceIncomplete)
        {
            // CMP-06: sin directorio completo no se envía a una parte. Se reintenta el slot hasta MaxFireDelay.
            if (withinMaxDelay)
                throw new CustomerAudienceUnavailableException(schedule.Id, command.SlotUtc);

            logger.LogError(
                "Schedule {ScheduleId} slot {Slot:o} skipped: client directory unavailable for over {MaxDelay}.",
                schedule.Id,
                command.SlotUtc,
                schedulerOptions.Value.MaxFireDelay
            );
            CampaignsMetrics.RecordScheduleFireSkipped("audience_incomplete");
            return await SkipAsync(command, opened.Error, bus, ct);
        }

        if (opened.IsFailure && opened.Error == CampaignScheduleErrors.LeaseLost)
            return Result.Failure<CampaignRunResponse>(opened.Error);

        if (opened.IsFailure)
        {
            // Regla de negocio (sin audiencia, campaña archivada): avanza sin run activo.
            logger.LogWarning(
                "Schedule {ScheduleId} (campaign {CampaignId}) fire failed: {Error}. Advancing.",
                schedule.Id,
                schedule.CampaignId,
                opened.Error.Code
            );
            return await SkipAsync(command, opened.Error, bus, ct);
        }

        if (!opened.Value.AwaitsFunding)
            return Result.Success(opened.Value.Run);

        var runId = opened.Value.Run.Id;
        var funding = await CampaignRunFunding.ReserveAsync(
            wallet,
            schedule.TenantId,
            runId,
            opened.Value.Billable,
            callerBearerToken: null,
            ct
        );

        if (funding.Outcome == RunFundingOutcome.WalletUnavailable)
        {
            // Transitorio: el slot no avanza. El lease vence y el próximo claim retoma el run con la misma referencia.
            if (withinMaxDelay)
            {
                logger.LogWarning(
                    "Schedule {ScheduleId} slot {Slot:o} waits for the Wallet; will retry.",
                    schedule.Id,
                    command.SlotUtc
                );
                return Result.Failure<CampaignRunResponse>(CampaignRunErrors.WalletUnavailable);
            }

            logger.LogError(
                "Schedule {ScheduleId} slot {Slot:o} gave up: Wallet unavailable for over {MaxDelay}.",
                schedule.Id,
                command.SlotUtc,
                schedulerOptions.Value.MaxFireDelay
            );
            CampaignsMetrics.RecordScheduleFireSkipped("wallet_unavailable");
        }

        var applied = await bus.InvokeAsync<Result<CampaignRunResponse>>(
            new ApplyCampaignRunFundingCommand(schedule.TenantId, runId, funding, slot, correlation.CorrelationId),
            ct
        );
        if (applied.IsSuccess)
            logger.LogInformation(
                "Fired schedule {ScheduleId} (campaign {CampaignId}) → run {RunId}.",
                schedule.Id,
                schedule.CampaignId,
                runId
            );
        return applied;
    }

    private static async Task<Result<CampaignRunResponse>> SkipAsync(
        FireCampaignScheduleCommand command,
        Error reason,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var skipped = await bus.InvokeAsync<Result>(
            new SkipScheduledSlotCommand(command.TenantId, command.ScheduleId, command.LeaseToken, command.SlotUtc),
            ct
        );
        return Result.Failure<CampaignRunResponse>(skipped.IsFailure ? skipped.Error : reason);
    }

    /// <summary>El run que ya tiene el slot (un intento anterior no terminó) o uno nuevo con la audiencia de ahora.</summary>
    private static async Task<Result<OpenedCampaignRun>> OpenRunAsync(
        CampaignSchedule schedule,
        ScheduledSlot slot,
        ICampaignRepository campaigns,
        ICampaignRunRepository runs,
        IContactRepository contacts,
        IContactListRepository lists,
        ICustomerAudienceClient customerClient,
        ICampaignCustomerAssignmentReader assignmentReader,
        CampaignsVisibilityOptions visibility,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var campaign = await campaigns.GetByIdAsync(schedule.TenantId, schedule.CampaignId, ct);
        if (campaign is null)
            return Result.Failure<OpenedCampaignRun>(CampaignErrors.NotFound);
        if (campaign.Status == CampaignStatus.Archived)
            return Result.Failure<OpenedCampaignRun>(CampaignErrors.Archived);

        var existing = await runs.GetByIdAsync(schedule.TenantId, ScheduledRunId.For(schedule.Id, slot.SlotUtc), ct);
        if (existing is not null)
            return existing.Status == CampaignRunStatus.Created
                ? Result.Success(OpenedCampaignRun.From(existing))
                : Result.Failure<OpenedCampaignRun>(CampaignRunErrors.NotCreated);

        // A1 — dispara en nombre de quien agendó (o del sucesor) y con SU visibilidad de clientes.
        var actor = schedule.EffectiveRunAsUserId ?? SchedulerActor;
        var resolved = await StartCampaignRunFromAudienceHandler.ResolveUnitsAsync(
            campaign,
            actor,
            schedule.ContactListIds(),
            [],
            schedule.IncludeCustomers,
            schedule.CustomerIds(),
            schedule.CreatorCanViewAllCustomers,
            contacts,
            lists,
            customerClient,
            assignmentReader,
            visibility,
            ct
        );
        if (resolved.IsFailure)
            return Result.Failure<OpenedCampaignRun>(resolved.Error);

        return await bus.InvokeAsync<Result<OpenedCampaignRun>>(
            new OpenCampaignRunCommand(schedule.TenantId, campaign.Id, actor, "Scheduled", resolved.Value, slot),
            ct
        );
    }
}
