using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Campaigns.Domain.Scheduling;

public enum ScheduleKind
{
    OneTime = 0,
    Recurring = 1,
}

public enum ScheduleStatus
{
    Active = 0,
    Paused = 1,
    Cancelled = 2,
    Completed = 3,
}

/// <summary>
/// Aggregate root del <b>agendado</b> de una campaña (SendMode Scheduled/Recurring — <c>State_Machines.md</c>).
/// Cada disparo crea un <c>CampaignRun</c> inmutable nuevo; el schedule NO ejecuta, solo dice "cuándo" y
/// "a qué audiencia" (ids de ContactList). Durable + con lease (no poll-sin-lease, anti-patrón legado).
/// Políticas: <b>misfire=coalesce</b> (dispara una vez y avanza al próximo slot futuro), <b>overlap=skip</b>
/// (no dispara si hay un run activo; se libera al consumir <c>run.completed</c>). SIN dinero.
/// Nota: recurrencia por intervalo (minutos) en este slice; cron/zona horaria/DST = refinamiento posterior.
/// </summary>
public sealed class CampaignSchedule : TenantEntity
{
    private CampaignSchedule() { }

    public Guid CampaignId { get; private set; }
    public ScheduleKind Kind { get; private set; }
    public ScheduleStatus Status { get; private set; }

    /// <summary>Próximo disparo (UTC). Null cuando está Completed/Cancelled.</summary>
    public DateTime? NextFireAtUtc { get; private set; }

    /// <summary>Intervalo de recurrencia en minutos (solo Recurring).</summary>
    public int? IntervalMinutes { get; private set; }

    /// <summary>Audiencia a resolver en cada disparo: ids de ContactList, CSV. Vacío = sin listas.</summary>
    public string ContactListIdsCsv { get; private set; } = string.Empty;

    /// <summary>Si true, cada disparo suma también los clientes activos del directorio de Customer (M2M).</summary>
    public bool IncludeCustomers { get; private set; }

    public DateTime? LastFiredAtUtc { get; private set; }

    /// <summary>Run en vuelo del último disparo (guard de solape). Se limpia al consumir <c>run.completed</c>.</summary>
    public Guid? ActiveRunId { get; private set; }

    // Lease de claim (concurrencia entre instancias del scheduler): token único por claim + expiración.
    public Guid? LeaseToken { get; private set; }
    public DateTime? LeasedUntilUtc { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Result<CampaignSchedule> CreateOneTime(
        Guid tenantId,
        Guid campaignId,
        DateTime runAtUtc,
        IReadOnlyCollection<Guid> contactListIds,
        bool includeCustomers = false
    )
    {
        var guard = BaseGuards(tenantId, campaignId);
        if (guard.IsFailure)
            return Result.Failure<CampaignSchedule>(guard.Error);
        if (runAtUtc == default)
            return Result.Failure<CampaignSchedule>(CampaignScheduleErrors.RunAtRequired);

        return Result.Success(
            New(tenantId, campaignId, ScheduleKind.OneTime, runAtUtc, null, contactListIds, includeCustomers)
        );
    }

    public static Result<CampaignSchedule> CreateRecurring(
        Guid tenantId,
        Guid campaignId,
        DateTime firstRunAtUtc,
        int intervalMinutes,
        IReadOnlyCollection<Guid> contactListIds,
        bool includeCustomers = false
    )
    {
        var guard = BaseGuards(tenantId, campaignId);
        if (guard.IsFailure)
            return Result.Failure<CampaignSchedule>(guard.Error);
        if (firstRunAtUtc == default)
            return Result.Failure<CampaignSchedule>(CampaignScheduleErrors.RunAtRequired);
        if (intervalMinutes <= 0)
            return Result.Failure<CampaignSchedule>(CampaignScheduleErrors.IntervalInvalid);

        return Result.Success(
            New(
                tenantId,
                campaignId,
                ScheduleKind.Recurring,
                firstRunAtUtc,
                intervalMinutes,
                contactListIds,
                includeCustomers
            )
        );
    }

    public Result Pause()
    {
        if (Status != ScheduleStatus.Active)
            return Result.Failure(CampaignScheduleErrors.NotActive);
        Status = ScheduleStatus.Paused;
        Touch();
        return Result.Success();
    }

    public Result Resume()
    {
        if (Status != ScheduleStatus.Paused)
            return Result.Failure(CampaignScheduleErrors.NotPaused);
        Status = ScheduleStatus.Active;
        Touch();
        return Result.Success();
    }

    public Result Cancel()
    {
        if (Status is ScheduleStatus.Cancelled or ScheduleStatus.Completed)
            return Result.Failure(CampaignScheduleErrors.InvalidTransition);
        Status = ScheduleStatus.Cancelled;
        NextFireAtUtc = null;
        ClearLease();
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Registra un disparo: guarda el run activo (guard de solape) y avanza. OneTime → Completed; Recurring
    /// → próximo slot FUTURO (coalescing de slots perdidos = misfire). Libera el lease.
    /// </summary>
    public void MarkFired(Guid runId, DateTime nowUtc)
    {
        LastFiredAtUtc = nowUtc;
        // runId vacío = el disparo no produjo run (p.ej. audiencia no resoluble): avanzar sin activar el
        // guard de solape (si no, quedaría bloqueado esperando un run.completed que nunca llega).
        if (runId != Guid.Empty)
            ActiveRunId = runId;

        if (Kind == ScheduleKind.OneTime)
        {
            Status = ScheduleStatus.Completed;
            NextFireAtUtc = null;
        }
        else
        {
            NextFireAtUtc = ComputeNextFuture(NextFireAtUtc ?? nowUtc, nowUtc, IntervalMinutes!.Value);
        }

        ClearLease();
        Touch();
    }

    /// <summary>Libera el guard de solape cuando el run correspondiente terminó.</summary>
    public void ReleaseActiveRun(Guid runId)
    {
        if (ActiveRunId == runId)
        {
            ActiveRunId = null;
            Touch();
        }
    }

    /// <summary>Marca el claim (lease) tomado por el scheduler; usado por el repo tras el UPDATE atómico.</summary>
    public bool IsClaimable(DateTime nowUtc) =>
        Status == ScheduleStatus.Active
        && NextFireAtUtc is { } next
        && next <= nowUtc
        && ActiveRunId is null
        && (LeasedUntilUtc is null || LeasedUntilUtc < nowUtc);

    public IReadOnlyList<Guid> ContactListIds() =>
        string.IsNullOrWhiteSpace(ContactListIdsCsv)
            ? []
            : ContactListIdsCsv
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty)
                .Where(g => g != Guid.Empty)
                .ToList();

    private void ClearLease()
    {
        LeaseToken = null;
        LeasedUntilUtc = null;
    }

    private static DateTime ComputeNextFuture(DateTime from, DateTime nowUtc, int intervalMinutes)
    {
        var next = from.AddMinutes(intervalMinutes);
        // Coalesce: si se perdieron varios slots (servicio caído), no dispares uno por cada uno — salta al próximo futuro.
        while (next <= nowUtc)
            next = next.AddMinutes(intervalMinutes);
        return next;
    }

    private static Result BaseGuards(Guid tenantId, Guid campaignId)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure(CampaignScheduleErrors.TenantRequired);
        if (campaignId == Guid.Empty)
            return Result.Failure(CampaignScheduleErrors.CampaignRequired);
        return Result.Success();
    }

    private static CampaignSchedule New(
        Guid tenantId,
        Guid campaignId,
        ScheduleKind kind,
        DateTime nextFireAtUtc,
        int? intervalMinutes,
        IReadOnlyCollection<Guid> contactListIds,
        bool includeCustomers
    )
    {
        var now = DateTime.UtcNow;
        var schedule = new CampaignSchedule
        {
            Id = Guid.NewGuid(),
            CampaignId = campaignId,
            Kind = kind,
            Status = ScheduleStatus.Active,
            NextFireAtUtc = nextFireAtUtc,
            IntervalMinutes = intervalMinutes,
            ContactListIdsCsv = string.Join(",", contactListIds.Where(g => g != Guid.Empty).Distinct()),
            IncludeCustomers = includeCustomers,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        schedule.SetTenant(tenantId);
        return schedule;
    }

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}
