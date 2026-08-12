namespace TaxVision.Reminder.Domain.Reminders;

/// <summary>
/// Las razones de cancelación que el servicio conoce. Existen como constantes porque
/// <see cref="Reminder.Cancel"/> las exige en su doc-comment y hasta ahora vivían como literales
/// sueltos repartidos entre el consumer de <c>reminder.target_closed.v1</c> y la API.
/// </summary>
public static class ReminderCancellationReasons
{
    /// <summary>Lo canceló la persona dueña del recordatorio.</summary>
    public const string UserRequest = "user_request";

    /// <summary>Se cerró el objetivo al que apuntaba (lo pone el consumer, nunca el usuario).</summary>
    public const string TargetClosed = "target_closed";

    /// <summary>Cualquier razón libre escrita por el usuario, colapsada para la métrica.</summary>
    public const string Other = "other";

    /// <summary>
    /// Colapsa la razón a una de las tres constantes de arriba <b>antes</b> de usarla como tag de
    /// una métrica. El endpoint de cancelar acepta texto libre, así que etiquetar con el valor crudo
    /// haría crecer sin techo la cardinalidad de la serie temporal: cada frase distinta que escriba
    /// un usuario sería una serie nueva en Prometheus. La razón completa se sigue guardando en la
    /// fila (<c>CancellationReason</c>), que es donde soporte la necesita.
    /// </summary>
    public static string ForMetrics(string? reason) =>
        reason?.Trim() switch
        {
            UserRequest => UserRequest,
            TargetClosed => TargetClosed,
            _ => Other,
        };
}
