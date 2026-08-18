using BuildingBlocks.Results;
using TaxVision.Calendar.Domain.Appointments;

namespace TaxVision.Calendar.Domain.ValueObjects;

/// <summary>
/// Donde ocurre la cita, en texto libre. La sala virtual no viaja aca: la crea Communication y llega
/// como <c>MeetingShortCode</c>.
/// </summary>
public sealed record Location
{
    public const int MaxLength = 300;

    public string Value { get; }

    private Location(string value) => Value = value;

    public static Result<Location> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result.Failure<Location>(AppointmentErrors.LocationEmpty);

        var trimmed = value.Trim();
        return trimmed.Length > MaxLength
            ? Result.Failure<Location>(AppointmentErrors.LocationTooLong)
            : Result.Success(new Location(trimmed));
    }

    public override string ToString() => Value;
}
