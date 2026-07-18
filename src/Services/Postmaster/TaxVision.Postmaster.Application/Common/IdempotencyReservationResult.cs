namespace TaxVision.Postmaster.Application.Common;

/// <summary>
/// Tri-state real del resultado de <see cref="IIdempotencyGuard.TryReserveAsync"/>. Reemplaza un
/// <c>Guid?</c> anterior que conflaba dos casos completamente distintos bajo el mismo <c>null</c>:
/// "reserva nueva, el caller debe crear el <c>SentMessage</c>" y "otro intento concurrente ya tiene la
/// reserva y puede seguir procesándose" — un caller que trataba ambos como "seguí adelante" terminaba
/// creando un segundo <c>SentMessage</c> ante una carrera real (doble-click en "Enviar", o Notification
/// republicando el evento antes de que el primer intento termine). <see cref="InProgress"/> existe como
/// caso explícito y distinto de <see cref="Reserved"/> precisamente para cerrar ese bug — no
/// simplificar de vuelta a un booleano.
/// </summary>
public enum IdempotencyReservationOutcome
{
    /// <summary>Reserva genuinamente nueva — el caller debe proceder y crear el <c>SentMessage</c>.</summary>
    Reserved,

    /// <summary>
    /// Un intento previo con esta clave ya terminó con éxito — el caller debe tratar esto como un
    /// replay limpio y devolver <see cref="IdempotencyReservationResult.ExistingSentMessageId"/> sin
    /// crear un <c>SentMessage</c> nuevo.
    /// </summary>
    AlreadyCompleted,

    /// <summary>
    /// Otro intento concurrente tiene la reserva tomada y todavía no terminó (dentro de la ventana de
    /// retry). El caller NO debe crear un <c>SentMessage</c> nuevo — este es el caso que antes se
    /// conflaba silenciosamente con <see cref="Reserved"/>.
    /// </summary>
    InProgress,
}

/// <summary>Resultado real de <see cref="IIdempotencyGuard.TryReserveAsync"/> — ver <see cref="IdempotencyReservationOutcome"/>.</summary>
public sealed record IdempotencyReservationResult(IdempotencyReservationOutcome Outcome, Guid? ExistingSentMessageId)
{
    public static IdempotencyReservationResult Reserved() => new(IdempotencyReservationOutcome.Reserved, null);

    public static IdempotencyReservationResult AlreadyCompleted(Guid sentMessageId) =>
        new(IdempotencyReservationOutcome.AlreadyCompleted, sentMessageId);

    public static IdempotencyReservationResult InProgress() => new(IdempotencyReservationOutcome.InProgress, null);
}
