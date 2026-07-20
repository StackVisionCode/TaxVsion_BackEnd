namespace TaxVision.Referrals.Application.Abstractions;

/// <summary>
/// Reserva atómica e idempotente de un slot anual. La implementación debe participar
/// en la misma transacción SQL, serializar por (ProgramId, ReferrerId, CalendarYear),
/// aceptar replay por QualificationId y rechazar cuando Count alcance Maximum.
/// </summary>
public interface IReferralRewardQuota
{
    Task<bool> TryReserveAnnualSlotAsync(
        Guid programId,
        Guid referrerId,
        int calendarYear,
        int maximum,
        Guid qualificationId,
        CancellationToken ct = default
    );
}
