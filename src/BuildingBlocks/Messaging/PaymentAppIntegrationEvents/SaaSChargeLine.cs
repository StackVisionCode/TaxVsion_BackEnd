namespace BuildingBlocks.Messaging.PaymentAppIntegrationEvents;

/// <summary>
/// Una cosa cobrada dentro de un cobro de la plataforma con varias (p. ej. pasar a anual: plan, add-ons y asientos).
/// El importe de la línea es cantidad × precio unitario; las líneas suman el total cobrado.
/// </summary>
/// <remarks><c>TargetKind</c> (Plan | Seat | AddOn) y <c>TargetId</c> dicen a qué pertenece la línea. Opcionales.</remarks>
public sealed record SaaSChargeLine(
    string Description,
    int Quantity,
    long UnitAmountCents,
    string? TargetKind = null,
    Guid? TargetId = null
);
