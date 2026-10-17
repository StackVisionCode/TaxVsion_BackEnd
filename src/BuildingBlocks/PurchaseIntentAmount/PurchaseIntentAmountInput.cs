namespace BuildingBlocks.PurchaseIntentAmount;

/// <summary>SEC-M1.C — tuple que firma el servicio dueño del intent y que PaymentApp verifica antes
/// de abrir el checkout. Cambiar cualquier campo invalida la firma, que es exactamente la propiedad
/// que queremos: un atacante que capture el token M2M no puede alterar amount/currency sin romper el
/// hmac, porque la secret vive sólo en servidor.</summary>
public sealed record PurchaseIntentAmountInput(
    Guid TenantId,
    PurchaseIntentKind Kind,
    Guid IntentId,
    long AmountCents,
    string Currency
);
