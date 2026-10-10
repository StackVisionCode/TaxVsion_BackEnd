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

    /// <summary>Clientes SELECCIONADOS a incluir en cada disparo (ids de Customer, CSV). No vacío ⇒ solo esos;
    /// vacío ⇒ según <see cref="IncludeCustomers"/>. La selección se congela al agendar.</summary>
    public string CustomerIdsCsv { get; private set; } = string.Empty;

    /// <summary>Frecuencia del recurrente (null en OneTime). Define cómo se calcula el próximo disparo.</summary>
    public RecurrenceFrequency? Frequency { get; private set; }

    /// <summary>Fin por fecha: cuando el próximo disparo caería después de este instante, el schedule se
    /// completa. Null = sin fecha de fin.</summary>
    public DateTime? EndsAtUtc { get; private set; }

    /// <summary>Fin por cantidad: tras <see cref="MaxOccurrences"/> disparos, se completa. Null = sin tope.</summary>
    public int? MaxOccurrences { get; private set; }

    /// <summary>Disparos ya realizados (para el tope por cantidad).</summary>
    public int OccurrenceCount { get; private set; }

    public DateTime? LastFiredAtUtc { get; private set; }

    /// <summary>Run en vuelo del último disparo (guard de solape). Se limpia al consumir <c>run.completed</c>.</summary>
    public Guid? ActiveRunId { get; private set; }

    // Lease de claim (concurrencia entre instancias del scheduler): token único por claim + expiración.
    public Guid? LeaseToken { get; private set; }
    public DateTime? LeasedUntilUtc { get; private set; }

    /// <summary>
    /// Quién agendó la campaña, y si en ese momento podía ver a TODOS los clientes de la oficina
    /// (<c>customers.view_all</c>).
    ///
    /// <para>
    /// A1 — el scheduler es un actor de sistema, así que disparaba cada corrida con la visibilidad
    /// abierta: un preparador que solo ve sus clientes asignados agendaba una campaña y el envío salía a
    /// la cartera completa de la oficina. La decisión de audiencia se toma al agendar; el disparo solo la
    /// ejecuta, así que la visibilidad se congela acá.
    /// </para>
    /// <para>
    /// <c>CreatorCanViewAllCustomers</c> es <c>true</c> para los schedules creados antes de esta fase: no
    /// hay forma de saber qué veía su creador, y asumir lo contrario cambiaría a quién se le envía un
    /// correo ya agendado (§R.7 del plan).
    /// </para>
    /// </summary>
    public Guid? CreatedByUserId { get; private set; }
    public bool CreatorCanViewAllCustomers { get; private set; } = true;

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Result<CampaignSchedule> CreateOneTime(
        Guid tenantId,
        Guid campaignId,
        DateTime runAtUtc,
        IReadOnlyCollection<Guid> contactListIds,
        bool includeCustomers = false,
        Guid? createdByUserId = null,
        bool creatorCanViewAllCustomers = true,
        IReadOnlyCollection<Guid>? customerIds = null
    )
    {
        var guard = BaseGuards(tenantId, campaignId);
        if (guard.IsFailure)
            return Result.Failure<CampaignSchedule>(guard.Error);
        if (runAtUtc == default)
            return Result.Failure<CampaignSchedule>(CampaignScheduleErrors.RunAtRequired);

        return Result.Success(
            New(
                tenantId,
                campaignId,
                ScheduleKind.OneTime,
                runAtUtc,
                null,
                contactListIds,
                includeCustomers,
                createdByUserId,
                creatorCanViewAllCustomers,
                customerIds
            )
        );
    }

    /// <summary>
    /// Crea un schedule recurrente con frecuencia nombrada (Hourly/Daily/Weekly/Monthly) o
    /// <see cref="RecurrenceFrequency.Custom"/> (que usa <paramref name="intervalMinutes"/>). El fin es
    /// opcional y por lo que ocurra primero: fecha (<paramref name="endsAtUtc"/>) o cantidad
    /// (<paramref name="maxOccurrences"/>).
    /// </summary>
    public static Result<CampaignSchedule> CreateRecurring(
        Guid tenantId,
        Guid campaignId,
        DateTime firstRunAtUtc,
        RecurrenceFrequency frequency,
        int? intervalMinutes,
        IReadOnlyCollection<Guid> contactListIds,
        bool includeCustomers = false,
        Guid? createdByUserId = null,
        bool creatorCanViewAllCustomers = true,
        IReadOnlyCollection<Guid>? customerIds = null,
        DateTime? endsAtUtc = null,
        int? maxOccurrences = null
    )
    {
        var guard = BaseGuards(tenantId, campaignId);
        if (guard.IsFailure)
            return Result.Failure<CampaignSchedule>(guard.Error);
        if (firstRunAtUtc == default)
            return Result.Failure<CampaignSchedule>(CampaignScheduleErrors.RunAtRequired);
        // Custom necesita un intervalo en minutos explícito; los presets nombrados lo ignoran.
        if (frequency == RecurrenceFrequency.Custom && (intervalMinutes is null || intervalMinutes <= 0))
            return Result.Failure<CampaignSchedule>(CampaignScheduleErrors.IntervalInvalid);
        if (endsAtUtc is { } ends && ends <= firstRunAtUtc)
            return Result.Failure<CampaignSchedule>(CampaignScheduleErrors.EndBeforeStart);
        if (maxOccurrences is { } max && max <= 0)
            return Result.Failure<CampaignSchedule>(CampaignScheduleErrors.MaxOccurrencesInvalid);

        // Para presets nombrados no persistimos IntervalMinutes (el cálculo es por calendario).
        var persistedInterval = frequency == RecurrenceFrequency.Custom ? intervalMinutes : null;

        return Result.Success(
            New(
                tenantId,
                campaignId,
                ScheduleKind.Recurring,
                firstRunAtUtc,
                persistedInterval,
                contactListIds,
                includeCustomers,
                createdByUserId,
                creatorCanViewAllCustomers,
                customerIds,
                frequency,
                endsAtUtc,
                maxOccurrences
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

        OccurrenceCount++;

        if (Kind == ScheduleKind.OneTime)
        {
            Status = ScheduleStatus.Completed;
            NextFireAtUtc = null;
        }
        else if (MaxOccurrences is { } max && OccurrenceCount >= max)
        {
            // Tope por cantidad alcanzado.
            Status = ScheduleStatus.Completed;
            NextFireAtUtc = null;
        }
        else
        {
            var next = ComputeNextFuture(NextFireAtUtc ?? nowUtc, nowUtc);
            // Fin por fecha: si el próximo slot cae después del fin, no quedan más disparos.
            if (EndsAtUtc is { } ends && next > ends)
            {
                Status = ScheduleStatus.Completed;
                NextFireAtUtc = null;
            }
            else
            {
                NextFireAtUtc = next;
            }
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

    public IReadOnlyList<Guid> ContactListIds() => ParseCsvGuids(ContactListIdsCsv);

    public IReadOnlyList<Guid> CustomerIds() => ParseCsvGuids(CustomerIdsCsv);

    private static IReadOnlyList<Guid> ParseCsvGuids(string csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? []
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty)
                .Where(g => g != Guid.Empty)
                .ToList();

    private void ClearLease()
    {
        LeaseToken = null;
        LeasedUntilUtc = null;
    }

    private DateTime ComputeNextFuture(DateTime from, DateTime nowUtc)
    {
        var next = Advance(from);
        // Coalesce: si se perdieron varios slots (servicio caído), no dispares uno por cada uno — salta al próximo futuro.
        while (next <= nowUtc)
            next = Advance(next);
        return next;
    }

    /// <summary>Avanza un instante al siguiente slot según la frecuencia. Monthly usa calendario
    /// (mismo día del mes siguiente, con clamp de fin de mes); Custom/legacy usan IntervalMinutes.</summary>
    private DateTime Advance(DateTime from) =>
        Frequency switch
        {
            RecurrenceFrequency.Hourly => from.AddHours(1),
            RecurrenceFrequency.Daily => from.AddDays(1),
            RecurrenceFrequency.Weekly => from.AddDays(7),
            RecurrenceFrequency.Monthly => from.AddMonths(1),
            // Custom, o schedules legacy sin Frequency: por intervalo en minutos.
            _ => from.AddMinutes(IntervalMinutes ?? 0),
        };

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
        bool includeCustomers,
        Guid? createdByUserId,
        bool creatorCanViewAllCustomers,
        IReadOnlyCollection<Guid>? customerIds,
        RecurrenceFrequency? frequency = null,
        DateTime? endsAtUtc = null,
        int? maxOccurrences = null
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
            Frequency = frequency,
            EndsAtUtc = endsAtUtc,
            MaxOccurrences = maxOccurrences,
            OccurrenceCount = 0,
            ContactListIdsCsv = string.Join(",", contactListIds.Where(g => g != Guid.Empty).Distinct()),
            IncludeCustomers = includeCustomers,
            CustomerIdsCsv = string.Join(",", (customerIds ?? []).Where(g => g != Guid.Empty).Distinct()),
            CreatedByUserId = createdByUserId,
            CreatorCanViewAllCustomers = creatorCanViewAllCustomers,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        schedule.SetTenant(tenantId);
        return schedule;
    }

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}
