using BuildingBlocks.Results;
using TaxVision.Reminder.Domain.Reminders;

namespace TaxVision.Reminder.Domain.ValueObjects;

/// <summary>
/// Clave de idempotencia (ADR-R-07), obligatoria. La entrega del bus es <i>at-least-once</i>: sin
/// esto un redelivery crea un recordatorio duplicado y al usuario le llega el aviso tres veces.
/// Es el fallo más probable de todo el diseño, por eso la clave es parte del modelo y no un detalle
/// del handler.
///
/// <para>
/// El soporte físico es el índice único <c>(TenantId, RequestKey)</c> de la Fase 2 — el VO solo
/// garantiza que la clave existe y tiene forma; la unicidad la garantiza la base de datos.
/// </para>
/// </summary>
public sealed record RequestKey
{
    public const int MaxLength = 200;

    private RequestKey(string value) => Value = value;

    public string Value { get; }

    public static Result<RequestKey> Create(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
            return Result.Failure<RequestKey>(ReminderErrors.RequestKey.Required);

        if (normalized.Length > MaxLength)
            return Result.Failure<RequestKey>(ReminderErrors.RequestKey.TooLong);

        return Result.Success(new RequestKey(normalized));
    }
}
