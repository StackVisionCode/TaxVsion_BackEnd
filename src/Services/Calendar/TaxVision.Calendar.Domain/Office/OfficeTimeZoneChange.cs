using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Calendar.Domain.Office;

/// <summary>
/// P9: la oficina cambió de zona. Calendar lo registra para ofrecer mover los horarios y las series que estaban en la
/// zona anterior; nada se mueve sin que un admin lo confirme, y las citas puntuales nunca.
///
/// <para>
/// Si llega otro cambio antes de decidir, el pendiente queda reemplazado y el nuevo conserva la zona de origen: lo que
/// hay que mover sigue en esa. Volver a la zona de origen no deja nada pendiente.
/// </para>
/// </summary>
public sealed class OfficeTimeZoneChange : AggregateRoot
{
    private OfficeTimeZoneChange() { }

    public string FromTimeZoneId { get; private set; } = string.Empty;
    public string ToTimeZoneId { get; private set; } = string.Empty;
    public DateTime ChangedAtUtc { get; private set; }

    /// <summary>Versión de la oficina del evento: un evento repetido no crea otro registro.</summary>
    public long TenantVersion { get; private set; }

    public OfficeTimeZoneChangeStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }

    [UserReference(UserReferenceKind.Provenance, Note = "Quién decidió; queda como registro.")]
    public Guid? ResolvedByUserId { get; private set; }

    public int MovedRules { get; private set; }
    public int MovedSeries { get; private set; }

    public static Result<OfficeTimeZoneChange?> Record(
        Guid tenantId,
        string previousTimeZoneId,
        string newTimeZoneId,
        DateTime changedAtUtc,
        long tenantVersion,
        OfficeTimeZoneChange? pending,
        DateTime nowUtc
    )
    {
        var from = pending?.FromTimeZoneId ?? previousTimeZoneId;
        pending?.Close(OfficeTimeZoneChangeStatus.Superseded, null, nowUtc);
        if (string.Equals(from, newTimeZoneId, StringComparison.Ordinal))
            return Result.Success<OfficeTimeZoneChange?>(null);

        var change = new OfficeTimeZoneChange
        {
            Id = Guid.NewGuid(),
            FromTimeZoneId = from,
            ToTimeZoneId = newTimeZoneId,
            ChangedAtUtc = changedAtUtc,
            TenantVersion = tenantVersion,
            Status = OfficeTimeZoneChangeStatus.Pending,
            CreatedAtUtc = nowUtc,
        };
        change.SetTenant(tenantId);
        return Result.Success<OfficeTimeZoneChange?>(change);
    }

    public bool IsPending => Status == OfficeTimeZoneChangeStatus.Pending;

    /// <summary>Llegó otro cambio y en la zona de origen ya no queda nada que mover.</summary>
    public void Supersede(DateTime nowUtc)
    {
        if (IsPending)
            Close(OfficeTimeZoneChangeStatus.Superseded, null, nowUtc);
    }

    /// <summary>El admin confirmó qué mover; queda cuánto se movió.</summary>
    public Result Apply(Guid byUserId, int movedRules, int movedSeries, DateTime nowUtc)
    {
        if (!IsPending)
            return Result.Failure(OfficeTimeZoneChangeErrors.NotPending);
        MovedRules = movedRules;
        MovedSeries = movedSeries;
        Close(OfficeTimeZoneChangeStatus.Applied, byUserId, nowUtc);
        return Result.Success();
    }

    /// <summary>El admin decidió no mover nada.</summary>
    public Result Dismiss(Guid byUserId, DateTime nowUtc)
    {
        if (!IsPending)
            return Result.Failure(OfficeTimeZoneChangeErrors.NotPending);
        Close(OfficeTimeZoneChangeStatus.Dismissed, byUserId, nowUtc);
        return Result.Success();
    }

    private void Close(OfficeTimeZoneChangeStatus status, Guid? byUserId, DateTime nowUtc)
    {
        Status = status;
        ResolvedAtUtc = nowUtc;
        ResolvedByUserId = byUserId;
    }
}
