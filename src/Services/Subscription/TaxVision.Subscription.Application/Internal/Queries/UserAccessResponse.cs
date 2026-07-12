namespace TaxVision.Subscription.Application.Internal.Queries;

/// <summary>
/// Contrato interno consultado por Auth al emitir el JWT (service-to-service, no expuesto
/// vía gateway). Combina el estado de la suscripción base del tenant con el seat vigente
/// del usuario, si tiene uno.
/// </summary>
public sealed record UserAccessResponse(
    Guid UserId,
    Guid TenantId,
    bool HasActiveSeat,
    Guid? SeatId,
    string? SeatStatus,
    string? SeatType,
    DateTime? SeatExpiresAtUtc,
    string SubscriptionStatus,
    string PlanCode
);
