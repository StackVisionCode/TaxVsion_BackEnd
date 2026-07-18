namespace TaxVision.Postmaster.Application.Common;

/// <summary>
/// Deduplicación por <c>(TenantId, IdempotencyKey)</c> antes de encolar cualquier envío — capa
/// obligatoria previa a crear un <c>SentMessage</c> (plan §Fase 4).
/// </summary>
public interface IIdempotencyGuard
{
    /// <summary>
    /// Reserva la clave para este envío. Devuelve un tri-state real (plan §Fase 11) —
    /// <see cref="IdempotencyReservationOutcome.Reserved"/>: la reserva es genuinamente nueva, el
    /// caller debe crear el <c>SentMessage</c>. <see cref="IdempotencyReservationOutcome.AlreadyCompleted"/>:
    /// el envío ya se completó antes, el caller debe devolver
    /// <see cref="IdempotencyReservationResult.ExistingSentMessageId"/> como replay limpio sin crear
    /// nada nuevo. <see cref="IdempotencyReservationOutcome.InProgress"/>: otro intento concurrente
    /// tiene la reserva tomada y no terminó — el caller NO debe crear un <c>SentMessage</c> nuevo, debe
    /// tratarlo como una falla transitoria (no un error del negocio).
    /// </summary>
    Task<IdempotencyReservationResult> TryReserveAsync(Guid tenantId, string idempotencyKey, CancellationToken ct);

    /// <summary>Cierra la reserva con el <c>SentMessageId</c> ya persistido, tras un envío exitoso.</summary>
    Task CompleteAsync(Guid tenantId, string idempotencyKey, Guid sentMessageId, CancellationToken ct);
}
