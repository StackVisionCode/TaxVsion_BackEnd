using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Runs.Events;

namespace TaxVision.Campaigns.Domain.Runs;

/// <summary>Datos de una unidad a materializar (una por destinatario/canal).</summary>
public sealed record RunRecipientDraft(
    string ContactRef,
    CampaignChannel Channel,
    string? Email,
    string? PhoneE164,
    string? Name = null
);

/// <summary>
/// Aggregate root de UNA ejecución inmutable de una campaña (<c>Domain_Design.md §4</c>,
/// <c>State_Machines.md §2</c>). SIN dinero. Slice 2: materialización síncrona de una lista de
/// unidades; cierre por conteo sobre el total congelado
/// (<c>delivered+accepted+failed+skipped+unknown == RecipientCount</c>). El paginado/checkpoint,
/// <c>CampaignRun</c> como fuente única de contadores-caché y la reconciliación tardía son fases
/// posteriores.
/// </summary>
public sealed class CampaignRun : AggregateRoot
{
    private readonly List<CampaignRecipient> _recipients = [];

    private CampaignRun() { }

    public Guid CampaignId { get; private set; }

    [UserReference(UserReferenceKind.Provenance, Note = "La audiencia se congela al iniciar; no dispara de nuevo.")]
    public Guid TriggeredByUserId { get; private set; }
    public string TriggerKind { get; private set; } = default!;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? FinishedAtUtc { get; private set; }
    public CampaignRunStatus Status { get; private set; }
    public string? RejectionReason { get; private set; }
    public int RecipientCount { get; private set; }

    /// <summary>Agendado y slot que dispararon este run (null en el envío manual).</summary>
    public Guid? ScheduleId { get; private set; }
    public DateTime? ScheduledSlotUtc { get; private set; }

    public int CounterDispatched { get; private set; }
    public int CounterAccepted { get; private set; }
    public int CounterDelivered { get; private set; }
    public int CounterFailed { get; private set; }
    public int CounterSkipped { get; private set; }
    public int CounterUnknown { get; private set; }

    /// <summary>
    /// Concurrencia optimista: N eventos <c>dispatch.result</c> concurrentes hacen read-modify-write
    /// del run; sin este token se pisan contadores/cierre. En conflicto, EF lanza
    /// <c>DbUpdateConcurrencyException</c> (transitoria → Wolverine reintenta, relee y converge).
    /// </summary>
    public byte[] RowVersion { get; private set; } = default!;

    public IReadOnlyCollection<CampaignRecipient> Recipients => _recipients.AsReadOnly();

    public static Result<CampaignRun> Start(
        Guid tenantId,
        Guid campaignId,
        Guid triggeredByUserId,
        string triggerKind,
        IReadOnlyCollection<RunRecipientDraft> units
    ) => Create(Guid.NewGuid(), tenantId, campaignId, triggeredByUserId, triggerKind, units);

    /// <summary>Run de un agendado: Id determinista por (agendado, slot), ver <see cref="ScheduledRunId"/>.</summary>
    public static Result<CampaignRun> StartScheduled(
        Guid tenantId,
        Guid campaignId,
        Guid triggeredByUserId,
        Guid scheduleId,
        DateTime scheduledSlotUtc,
        IReadOnlyCollection<RunRecipientDraft> units
    )
    {
        var result = Create(
            ScheduledRunId.For(scheduleId, scheduledSlotUtc),
            tenantId,
            campaignId,
            triggeredByUserId,
            "Scheduled",
            units
        );
        if (result.IsSuccess)
        {
            result.Value.ScheduleId = scheduleId;
            result.Value.ScheduledSlotUtc = DateTime.SpecifyKind(scheduledSlotUtc, DateTimeKind.Utc);
        }
        return result;
    }

    private static Result<CampaignRun> Create(
        Guid id,
        Guid tenantId,
        Guid campaignId,
        Guid triggeredByUserId,
        string triggerKind,
        IReadOnlyCollection<RunRecipientDraft> units
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<CampaignRun>(CampaignRunErrors.TenantRequired);
        if (campaignId == Guid.Empty)
            return Result.Failure<CampaignRun>(CampaignRunErrors.CampaignRequired);
        if (units is null || units.Count == 0)
            return Result.Failure<CampaignRun>(CampaignRunErrors.NoRecipients);

        var run = new CampaignRun
        {
            Id = id,
            CampaignId = campaignId,
            TriggeredByUserId = triggeredByUserId,
            TriggerKind = triggerKind,
            CreatedAtUtc = DateTime.UtcNow,
            Status = CampaignRunStatus.Dispatching,
        };
        run.SetTenant(tenantId);

        foreach (var unit in units)
        {
            run._recipients.Add(
                CampaignRecipient.Materialize(
                    run.Id,
                    tenantId,
                    unit.ContactRef,
                    unit.Channel,
                    unit.Email,
                    unit.PhoneE164,
                    unit.Name
                )
            );
        }

        run.RecipientCount = run._recipients.Count;
        run.RecomputeCounters();
        run.EvaluateClosure(); // caso borde: todas las unidades Skipped al materializar → cierra ya
        return Result.Success(run);
    }

    /// <summary>
    /// Rechaza el run ANTES de despachar nada (PEP money-OUT, F4): el gate del Wallet no autorizó el cobro
    /// (saldo insuficiente o Wallet no disponible). Solo válido mientras está <see cref="CampaignRunStatus.Dispatching"/>
    /// y no se emitió ningún dispatch — así no se cobra ni se envía nada. Terminal: no publica run.completed
    /// (no hubo envío que liquidar).
    /// </summary>
    public Result Reject(string reason)
    {
        if (Status != CampaignRunStatus.Dispatching)
            return Result.Failure(CampaignRunErrors.NotDispatching);
        if (CounterDispatched > 0)
            return Result.Failure(CampaignRunErrors.AlreadyDispatched);

        Status = CampaignRunStatus.Rejected;
        RejectionReason = reason;
        FinishedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>
    /// El envío arranca: el gate del Wallet ya autorizó. Un run rechazado no pasa por acá, así que no se anuncia.
    /// Si ya cerró al materializar (todo Skipped), su cierre sale después del arranque.
    /// </summary>
    public void AnnounceStart()
    {
        AddDomainEvent(new CampaignRunStartedDomainEvent(Id, TenantId, CampaignId, RecipientCount, TriggerKind));
        if (Status is CampaignRunStatus.Completed or CampaignRunStatus.PartiallyFailed or CampaignRunStatus.Failed)
            AnnounceClosure();
    }

    /// <summary>Marca una unidad como despachada (tras emitir su evento de dispatch).</summary>
    public Result MarkDispatched(Guid recipientId)
    {
        var recipient = _recipients.Find(r => r.Id == recipientId);
        if (recipient is null)
            return Result.Failure(CampaignRunErrors.RecipientNotFound);

        recipient.MarkDispatched();
        RecomputeCounters();
        return Result.Success();
    }

    /// <summary>
    /// Aplica un resultado de dispatch (idempotente por <c>dispatchId</c>). Devuelve <c>true</c>
    /// si ESTA llamada cerró el run (para publicar <c>run.completed</c> una sola vez).
    /// </summary>
    public Result<bool> ApplyResult(string dispatchId, DispatchOutcome outcome, string? providerRef, string? reason) =>
        Apply(_recipients.Find(r => r.DispatchId == dispatchId), outcome, providerRef, reason);

    /// <summary>
    /// Variante por <c>recipientId</c> (Guid) para DLR cuyo seam de correlación es un Guid opaco y no
    /// el <c>DispatchId</c> string — hoy el DLR de Email (Postmaster reenvía <c>CampaignId</c> = el
    /// RecipientId). Misma semántica idempotente/tardía que <see cref="ApplyResult(string,DispatchOutcome,string?,string?)"/>.
    /// </summary>
    public Result<bool> ApplyResultForRecipient(
        Guid recipientId,
        DispatchOutcome outcome,
        string? providerRef,
        string? reason
    ) => Apply(_recipients.Find(r => r.Id == recipientId), outcome, providerRef, reason);

    /// <summary>
    /// Barrido de runs que no cierran: cada unidad sin asentar pasa a <c>Unknown</c> (<c>stale_timeout</c>) y
    /// se evalúa el cierre. Devuelve <c>true</c> solo si esta llamada cerró el run. Un run cerrado no cambia.
    /// </summary>
    public bool ExpireStaleUnits(DateTime nowUtc)
    {
        if (Status != CampaignRunStatus.Dispatching)
            return false;

        foreach (var recipient in _recipients.Where(r => !r.IsSettled))
            recipient.ExpireAsUnknown("stale_timeout", nowUtc);
        RecomputeCounters();
        var closedNow = EvaluateClosure();
        if (closedNow)
            AnnounceClosure();
        return closedNow;
    }

    private Result<bool> Apply(
        CampaignRecipient? recipient,
        DispatchOutcome outcome,
        string? providerRef,
        string? reason
    )
    {
        if (recipient is null)
            return Result.Failure<bool>(CampaignRunErrors.RecipientNotFound);

        recipient.ApplyOutcome(outcome, providerRef, reason);
        RecomputeCounters();
        var closedNow = EvaluateClosure();
        if (closedNow)
            AnnounceClosure();
        return Result.Success(closedNow);
    }

    // Foto del cierre: una unidad resuelta después (D-CMP-4) no cambia lo que se anunció.
    private void AnnounceClosure() =>
        AddDomainEvent(
            new CampaignRunClosedDomainEvent(
                Id,
                TenantId,
                CampaignId,
                Status,
                RecipientCount,
                CounterDelivered,
                CounterAccepted,
                CounterFailed,
                CounterSkipped,
                CounterUnknown
            )
        );

    private void RecomputeCounters()
    {
        CounterDispatched = _recipients.Count(r => r.State == DispatchState.Dispatched);
        CounterAccepted = _recipients.Count(r => r.State == DispatchState.Accepted);
        CounterDelivered = _recipients.Count(r => r.State == DispatchState.Delivered);
        CounterFailed = _recipients.Count(r => r.State == DispatchState.Failed);
        CounterSkipped = _recipients.Count(r => r.State == DispatchState.Skipped);
        CounterUnknown = _recipients.Count(r => r.State == DispatchState.Unknown);
    }

    /// <summary>Cierra el run si todas las unidades están settled. Devuelve <c>true</c> solo en la transición.</summary>
    private bool EvaluateClosure()
    {
        // D-CMP-4: una unidad resuelta después del cierre (Unknown → Accepted/Delivered/Failed) recalcula el
        // estado. No es un cierre nuevo: no vuelve a publicar run.completed y conserva FinishedAtUtc.
        if (Status is CampaignRunStatus.Completed or CampaignRunStatus.PartiallyFailed or CampaignRunStatus.Failed)
        {
            Status = TerminalStatus();
            return false;
        }

        if (Status != CampaignRunStatus.Dispatching)
            return false;
        if (_recipients.Any(r => !r.IsSettled))
            return false;

        Status = TerminalStatus();
        FinishedAtUtc = DateTime.UtcNow;
        return true;
    }

    // Sin fallos ni desconocidos → Completed (cubre todo entregado/aceptado y también el caso
    // "todo Skipped" legítimo: opt-out/sin destino no es un fallo del run). Solo si NO hubo
    // ningún entregado/aceptado y sí hubo fallos/desconocidos → Failed; el resto → PartiallyFailed.
    private CampaignRunStatus TerminalStatus() =>
        CounterFailed + CounterUnknown == 0 ? CampaignRunStatus.Completed
        : CounterDelivered + CounterAccepted == 0 ? CampaignRunStatus.Failed
        : CampaignRunStatus.PartiallyFailed;
}
