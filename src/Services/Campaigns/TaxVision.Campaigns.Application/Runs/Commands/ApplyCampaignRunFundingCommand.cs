using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Campaigns.Application.Campaigns.Abstractions;
using TaxVision.Campaigns.Application.Runs.Abstractions;
using TaxVision.Campaigns.Application.Scheduling.Abstractions;
using TaxVision.Campaigns.Application.Senders.Abstractions;
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
        ICampaignRepository campaigns,
        ICampaignScheduleRepository schedules,
        ISenderProfileRepository senderProfiles,
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
        var campaign = await campaigns.GetByIdAsync(command.TenantId, run.CampaignId, ct);
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
                var dispatches = await StartDispatchingAsync(run, campaign, senderProfiles, command.CorrelationId, ct);
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

    /// <summary>Pasa el run a despacho y arma un aviso por unidad, que sale después de guardar.</summary>
    private static async Task<IReadOnlyList<CampaignDispatchRequestedIntegrationEvent>> StartDispatchingAsync(
        CampaignRun run,
        Campaign campaign,
        ISenderProfileRepository senderProfiles,
        string? correlationId,
        CancellationToken ct
    )
    {
        run.MarkFundsReserved();

        // Resuelve el remitente (SenderRef opaco) por canal desde la selección de la campaña. Solo perfiles
        // Active cuentan; un canal sin selección va con SenderRef null (el ejecutor usa su default).
        var senderRefByChannel = await ResolveSenderRefsAsync(campaign, senderProfiles, ct);

        // Run.started y, si ya cerró al materializar, run.completed: salen de sus handlers de dominio.
        run.AnnounceStart();

        var dispatches = new List<CampaignDispatchRequestedIntegrationEvent>();
        foreach (var recipient in run.Recipients.Where(r => r.State == DispatchState.Pending).ToList())
        {
            // Contenido POR CANAL: usa el override del canal si existe; si no, cae al Message/Subject base
            // (compatibilidad). Para Push el título viaja en Subject (el evento solo tiene Subject+Body).
            var (subject, body) = ResolveChannelContent(campaign, recipient.Channel);
            dispatches.Add(
                new CampaignDispatchRequestedIntegrationEvent
                {
                    TenantId = run.TenantId,
                    CorrelationId = correlationId,
                    CampaignId = run.CampaignId,
                    RunId = run.Id,
                    RecipientId = recipient.Id,
                    AttemptNo = recipient.AttemptNo,
                    DispatchId = recipient.DispatchId,
                    Channel = recipient.Channel.ToString(),
                    ContactRef = recipient.ContactRef,
                    Email = recipient.Email,
                    PhoneE164 = recipient.PhoneE164,
                    RecipientName = recipient.Name,
                    SenderRef = senderRefByChannel.GetValueOrDefault(recipient.Channel),
                    // Snapshot inmutable del contenido resuelto por canal (Scribe = fase posterior).
                    Subject = subject,
                    Body = body,
                }
            );
            run.MarkDispatched(recipient.Id);
        }

        return dispatches;
    }

    /// <summary>
    /// Resuelve (Subject, Body) para un canal: usa el <see cref="CampaignContent"/> propio del canal si existe
    /// (para Push el título se mapea a Subject, ya que el evento solo lleva Subject+Body), o cae al
    /// <c>Subject</c>/<c>Message</c> base de la campaña (compatibilidad con campañas sin contenido por canal).
    /// </summary>
    private static (string? Subject, string Body) ResolveChannelContent(Campaign campaign, CampaignChannel channel)
    {
        var content = campaign.GetContentFor(channel);
        if (content is null)
            return (campaign.Subject, campaign.Message);
        return (content.Subject ?? content.Title, content.Body);
    }

    /// <summary>Mapa canal → <c>SenderRef</c> a partir de la selección de la campaña (solo perfiles Active).</summary>
    private static async Task<IReadOnlyDictionary<CampaignChannel, string?>> ResolveSenderRefsAsync(
        Campaign campaign,
        ISenderProfileRepository senderProfiles,
        CancellationToken ct
    )
    {
        if (campaign.Senders.Count == 0)
            return new Dictionary<CampaignChannel, string?>();

        var ids = campaign.Senders.Select(s => s.SenderProfileId).Distinct().ToList();
        var profiles = await senderProfiles.GetManyByIdsAsync(campaign.TenantId, ids, ct);
        var refById = profiles.Where(p => p.IsActive).ToDictionary(p => p.Id, p => p.SenderRef);

        var map = new Dictionary<CampaignChannel, string?>();
        foreach (var selection in campaign.Senders)
            if (refById.TryGetValue(selection.SenderProfileId, out var senderRef))
                map[selection.Channel] = senderRef;
        return map;
    }
}
