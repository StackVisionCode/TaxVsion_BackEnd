using BuildingBlocks.Results;
using TaxVision.Calendar.Domain.Appointments;

namespace TaxVision.Calendar.Domain.ValueObjects;

/// <summary>Titulo de la cita. Es lo que ve el asistente en la invitacion y en el archivo .ics.</summary>
public sealed record AppointmentTitle
{
    public const int MaxLength = 200;

    public string Value { get; }

    private AppointmentTitle(string value) => Value = value;

    public static Result<AppointmentTitle> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result.Failure<AppointmentTitle>(AppointmentErrors.TitleEmpty);

        var trimmed = value.Trim();
        return trimmed.Length > MaxLength
            ? Result.Failure<AppointmentTitle>(AppointmentErrors.TitleTooLong)
            : Result.Success(new AppointmentTitle(trimmed));
    }

    public override string ToString() => Value;
}
