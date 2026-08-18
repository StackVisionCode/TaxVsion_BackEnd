using BuildingBlocks.Results;
using BuildingBlocks.TimeZones;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Domain.ValueObjects;

/// <summary>
/// Vencimiento: el instante en UTC más la zona en que el usuario lo escribió. El instante es lo que
/// ordenan los índices; la zona es lo que se muestra y con lo que se recalcula si la fecha se mueve.
///
/// <para><see cref="IsStatutory"/> son los que impone la ley (15-abr, prórroga 15-oct, 1040-ES y 941
/// trimestrales). Aflojarlos exige razón en <see cref="Tasks.TaskItem.ChangeDue"/>.</para>
/// </summary>
public sealed record DueDate
{
    public DateTime DueAtUtc { get; }
    public string TimeZoneId { get; }
    public bool IsStatutory { get; }

    private DueDate(DateTime dueAtUtc, string timeZoneId, bool isStatutory)
    {
        DueAtUtc = dueAtUtc;
        TimeZoneId = timeZoneId;
        IsStatutory = isStatutory;
    }

    public static Result<DueDate> Create(DateTime dueAtUtc, string? timeZoneId, bool isStatutory)
    {
        if (dueAtUtc.Kind != DateTimeKind.Utc)
            return Result.Failure<DueDate>(TaskErrors.Due.NotUtc);

        // IanaTimeZone ya resuelve el mapeo IANA↔Windows: no duplicar la validación acá.
        if (!IanaTimeZone.TryNormalize(timeZoneId, out var normalizedTimeZoneId))
            return Result.Failure<DueDate>(TaskErrors.Due.TimeZoneInvalid);

        return Result.Success(new DueDate(dueAtUtc, normalizedTimeZoneId, isStatutory));
    }
}
