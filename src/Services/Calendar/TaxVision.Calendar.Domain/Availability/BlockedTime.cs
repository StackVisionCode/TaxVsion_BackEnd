using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Calendar.Domain.Availability;

/// <summary>
/// Una ausencia puntual: vacaciones, capacitacion, el almuerzo de un dia concreto.
///
/// <para>
/// A diferencia del solapamiento entre citas, esto <b>siempre bloquea</b>: si el preparador esta de
/// vacaciones, agendarle encima no es una advertencia que se pueda ignorar.
/// </para>
/// </summary>
public sealed class BlockedTime : AggregateRoot
{
    public const int MaxReasonLength = 200;

    public Guid UserId { get; private set; }

    public DateTime StartUtc { get; private set; }

    public DateTime EndUtc { get; private set; }

    public string? Reason { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    private BlockedTime() { }

    public static Result<BlockedTime> Create(
        Guid tenantId,
        Guid userId,
        DateTime startUtc,
        DateTime endUtc,
        string? reason,
        DateTime nowUtc
    )
    {
        if (userId == Guid.Empty)
            return Result.Failure<BlockedTime>(AvailabilityErrors.UserRequired);

        if (startUtc.Kind != DateTimeKind.Utc || endUtc.Kind != DateTimeKind.Utc)
            return Result.Failure<BlockedTime>(Appointments.TimingErrors.NotUtc);

        if (endUtc <= startUtc)
            return Result.Failure<BlockedTime>(AvailabilityErrors.EndBeforeStart);

        var trimmed = reason?.Trim();
        if (trimmed is { Length: > MaxReasonLength })
            return Result.Failure<BlockedTime>(AvailabilityErrors.ReasonTooLong);

        var block = new BlockedTime
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            StartUtc = startUtc,
            EndUtc = endUtc,
            Reason = string.IsNullOrEmpty(trimmed) ? null : trimmed,
            CreatedAtUtc = nowUtc,
        };
        block.SetTenant(tenantId);

        return Result.Success(block);
    }

    public bool Overlaps(DateTime startUtc, DateTime endUtc) => startUtc < EndUtc && endUtc > StartUtc;
}
