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
using TaxVision.Campaigns.Application.Senders.Abstractions;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Runs;
using TaxVision.Campaigns.Domain.Scheduling;
using Wolverine;

namespace TaxVision.Campaigns.Application.Scheduling.Commands;

/// <summary>Dispara el slot <paramref name="SlotUtc"/> de un agendado reclamado con <paramref name="LeaseToken"/>.</summary>
public sealed record FireCampaignScheduleCommand(Guid TenantId, Guid ScheduleId, Guid LeaseToken, DateTime SlotUtc);

/// <summary>
/// Disparo del agendado en UNA transacción de Wolverine: run, unidades, avance del agendado y sobres del
/// outbox se confirman juntos o no se confirma nada (CMP-01). El run lleva Id determinista por slot.
/// </summary>
public static class FireCampaignScheduleHandler
{
    // Actor de sistema cuando el agendado no tiene creador.
    private static readonly Guid SchedulerActor = new("5c4ed011-0000-0000-0000-0000005c4ed0");

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
        ISenderProfileRepository senderProfiles,
        IWalletSpendClient wallet,
        IUnitOfWork unitOfWork,
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

        var nowUtc = DateTime.UtcNow;
        var started = await StartRunAsync(
            schedule,
            command.SlotUtc,
            campaigns,
            runs,
            contacts,
            lists,
            customerClient,
            assignmentReader,
            visibility.Value,
            senderProfiles,
            wallet,
            unitOfWork,
            bus,
            correlation,
            ct
        );

        if (started.IsFailure && started.Error == CampaignRunErrors.AudienceIncomplete)
        {
            // CMP-06: sin directorio completo no se envía a una parte. Se reintenta el slot hasta MaxFireDelay.
            if (nowUtc - command.SlotUtc <= schedulerOptions.Value.MaxFireDelay)
                throw new CustomerAudienceUnavailableException(schedule.Id, command.SlotUtc);

            logger.LogError(
                "Schedule {ScheduleId} slot {Slot:o} skipped: client directory unavailable for over {MaxDelay}.",
                schedule.Id,
                command.SlotUtc,
                schedulerOptions.Value.MaxFireDelay
            );
            CampaignsMetrics.RecordScheduleFireSkipped("audience_incomplete");
            schedule.MarkFired(Guid.Empty, nowUtc);
            await unitOfWork.SaveChangesAsync(ct);
            return started;
        }

        if (started.IsFailure)
        {
            // Regla de negocio (sin audiencia, Wallet rechaza, campaña archivada): avanza sin run activo.
            logger.LogWarning(
                "Schedule {ScheduleId} (campaign {CampaignId}) fire failed: {Error}. Advancing.",
                schedule.Id,
                schedule.CampaignId,
                started.Error.Code
            );
            schedule.MarkFired(Guid.Empty, nowUtc);
            await unitOfWork.SaveChangesAsync(ct);
            return started;
        }

        var runIsTerminal =
            Enum.TryParse<CampaignRunStatus>(started.Value.Status, out var status)
            && status
                is CampaignRunStatus.Completed
                    or CampaignRunStatus.PartiallyFailed
                    or CampaignRunStatus.Failed
                    or CampaignRunStatus.Rejected;
        schedule.MarkFired(started.Value.Id, nowUtc, runIsTerminal);
        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation(
            "Fired schedule {ScheduleId} (campaign {CampaignId}) → run {RunId}; next={Next:o}.",
            schedule.Id,
            schedule.CampaignId,
            started.Value.Id,
            schedule.NextFireAtUtc
        );
        return started;
    }

    private static async Task<Result<CampaignRunResponse>> StartRunAsync(
        CampaignSchedule schedule,
        DateTime slotUtc,
        ICampaignRepository campaigns,
        ICampaignRunRepository runs,
        IContactRepository contacts,
        IContactListRepository lists,
        ICustomerAudienceClient customerClient,
        ICampaignCustomerAssignmentReader assignmentReader,
        CampaignsVisibilityOptions visibility,
        ISenderProfileRepository senderProfiles,
        IWalletSpendClient wallet,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var campaign = await campaigns.GetByIdAsync(schedule.TenantId, schedule.CampaignId, ct);
        if (campaign is null)
            return Result.Failure<CampaignRunResponse>(CampaignErrors.NotFound);
        if (campaign.Status == CampaignStatus.Archived)
            return Result.Failure<CampaignRunResponse>(CampaignErrors.Archived);

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
            return Result.Failure<CampaignRunResponse>(resolved.Error);

        return await StartCampaignRunHandler.StartAndDispatchAsync(
            campaign,
            "Scheduled",
            actor,
            resolved.Value,
            runs,
            senderProfiles,
            wallet,
            callerBearerToken: null,
            unitOfWork,
            bus,
            correlation,
            ct,
            new ScheduledSlot(schedule.Id, slotUtc)
        );
    }
}
