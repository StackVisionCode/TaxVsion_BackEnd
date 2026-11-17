namespace TaxVision.Referrals.Application.Abstractions;

/// <summary>
/// Reserva atómica e idempotente de un slot anual. La implementación debe participar
/// en la misma transacción SQL, serializar por (ProgramId, ReferrerId, CalendarYear),
/// aceptar replay por QualificationId y rechazar cuando Count alcance Maximum.
/// </summary>
/// <remarks>
/// <paramref name="ownerTenantId"/> es explícito porque este servicio puede correr dentro de un
/// handler de Wolverine (bus.InvokeAsync) donde el ITenantContext ambiental no está disponible
/// en el nuevo scope de DI. En T2T la cuota es propiedad del <b>tenant del referrer</b> — no
/// del tenant activo — así que además de resolver el bug de scope, se explicita el owner real.
/// <paramref name="actingTenantId"/> es el tenant de quien paga (dueño de la atribución): la calificación corre con
/// ese tenant activo, así que es un contexto válido aunque no sea el dueño de la cuota.
/// </remarks>
public interface IReferralRewardQuota
{
    Task<bool> TryReserveAnnualSlotAsync(
        Guid ownerTenantId,
        Guid programId,
        Guid referrerId,
        int calendarYear,
        int maximum,
        Guid qualificationId,
        Guid actingTenantId,
        CancellationToken ct = default
    );
}
