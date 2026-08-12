using BuildingBlocks.Messaging;
using Microsoft.Extensions.Logging;
using TaxVision.Reminder.Domain.ValueObjects;

namespace TaxVision.Reminder.Application.Reminders.Consumers;

/// <summary>
/// Lo que los tres consumers de entrada hacen igual: resolver la correlación y traducir la categoría
/// que viaja como texto.
/// </summary>
internal static class ReminderInboundEvent
{
    /// <summary>
    /// Correlación del evento, o el <c>EventId</c> si el publicador no la propagó. Nunca vacía: un
    /// log sin correlación es un log que no se puede seguir a través de tres servicios.
    /// </summary>
    internal static string CorrelationOf(IIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;

    /// <summary>
    /// <c>Category</c> viaja como texto para que sumar una categoría no obligue a redesplegar a los
    /// publicadores (02_Contratos §1.1). El precio es este parseo, y la regla que lo acompaña: una
    /// categoría desconocida se <b>descarta con log</b>, no revienta el consumer — si lanzara,
    /// Wolverine reintentaría hasta la DLQ un evento que ninguna reintento puede arreglar.
    ///
    /// <para>
    /// Se rechaza el valor numérico crudo («2») que <c>Enum.TryParse</c> aceptaría: dejarlo pasar
    /// ataría a los publicadores al orden de los miembros del enum, que es justo el acoplamiento que
    /// el string vino a evitar.
    /// </para>
    /// </summary>
    internal static bool TryParseCategory(string? raw, out ReminderCategory category)
    {
        category = default;

        if (string.IsNullOrWhiteSpace(raw) || char.IsAsciiDigit(raw.Trim()[0]))
            return false;

        return Enum.TryParse(raw.Trim(), ignoreCase: true, out category) && Enum.IsDefined(category);
    }

    internal static void LogUnknownCategory(ILogger logger, string? raw, Guid eventId, string eventName) =>
        logger.LogWarning(
            "{EventName} {EventId} carries an unknown category '{Category}'; discarding it instead of retrying.",
            eventName,
            eventId,
            raw
        );
}
