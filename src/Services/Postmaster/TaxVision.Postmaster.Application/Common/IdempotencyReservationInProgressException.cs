namespace TaxVision.Postmaster.Application.Common;

/// <summary>
/// Señala que un envío con esta <c>IdempotencyKey</c> está siendo procesado por otro intento
/// concurrente (carrera real de idempotencia, plan §Fase 11) — nunca "algo salió mal" en el sentido
/// tradicional. La lanza <see cref="Consumers.NotificationsEmailSendRequestedConsumer"/> tanto cuando
/// <see cref="IIdempotencyGuard.TryReserveAsync"/> devuelve <see cref="IdempotencyReservationOutcome.InProgress"/>
/// como cuando el índice único de <c>SentMessages</c> revienta con <c>ConflictException</c> en la
/// ventana angosta donde dos reservas pueden leerse como "no existe" antes de que cualquiera escriba.
/// Deliberadamente una excepción (no un <c>Result</c>) para que la política global de retry+cooldown de
/// Wolverine (<c>Program.cs</c>, <c>OnException&lt;Exception&gt;</c>) reintente el mensaje completo — la
/// próxima vuelta encontrará <see cref="IdempotencyReservationOutcome.AlreadyCompleted"/> si el ganador
/// ya terminó, en vez de crear un segundo <c>SentMessage</c>.
/// </summary>
public sealed class IdempotencyReservationInProgressException(string idempotencyKey)
    : Exception($"Idempotency reservation for key '{idempotencyKey}' is still in progress; retry expected.")
{
    public string IdempotencyKey { get; } = idempotencyKey;
}
