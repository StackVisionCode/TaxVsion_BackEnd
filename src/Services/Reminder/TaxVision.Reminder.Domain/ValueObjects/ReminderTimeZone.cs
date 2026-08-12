using BuildingBlocks.Results;
using BuildingBlocks.TimeZones;
using TaxVision.Reminder.Domain.Reminders;

namespace TaxVision.Reminder.Domain.ValueObjects;

/// <summary>
/// Zona horaria IANA validada (ADR-R-06). La trae el request; Reminder no la infiere ni la busca en
/// una proyección de usuarios, porque Auth hoy no publica la tz del usuario. Si algún día la
/// publica, se suma como <i>default</i> sin romper el contrato.
///
/// <para>
/// Reutiliza <see cref="IanaTimeZone"/> de BuildingBlocks — ya resuelve el mapeo IANA↔Windows, que
/// es la parte que se hace mal cuando alguien escribe su propio validador.
/// </para>
/// </summary>
public sealed record ReminderTimeZone
{
    private ReminderTimeZone(string value) => Value = value;

    public string Value { get; }

    public static ReminderTimeZone Utc => new(IanaTimeZone.UtcId);

    public static Result<ReminderTimeZone> Create(string? ianaId)
    {
        if (!IanaTimeZone.TryNormalize(ianaId, out var normalized))
            return Result.Failure<ReminderTimeZone>(ReminderErrors.TimeZone.Invalid);

        return Result.Success(new ReminderTimeZone(normalized));
    }
}
