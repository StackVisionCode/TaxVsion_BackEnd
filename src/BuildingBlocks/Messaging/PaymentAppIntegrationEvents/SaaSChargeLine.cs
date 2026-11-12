namespace BuildingBlocks.Messaging.PaymentAppIntegrationEvents;

/// <summary>
/// Una cosa cobrada dentro de un cobro de la plataforma con varias (p. ej. pasar a anual: plan, add-ons y asientos).
/// El importe de la línea es cantidad × precio unitario; las líneas suman el total cobrado.
/// </summary>
public sealed record SaaSChargeLine(string Description, int Quantity, long UnitAmountCents);
