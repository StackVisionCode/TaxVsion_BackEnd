namespace BuildingBlocks.PurchaseIntentAmount;

/// <summary>SEC-M1.C — tipo de purchase intent. El valor del enum entra en la firma HMAC (nombre
/// textual, estable), así que renombrar un miembro invalida firmas en vuelo — hacerlo sólo con
/// despliegue coordinado (previous-secret TTL cubre la ventana).</summary>
public enum PurchaseIntentKind
{
    PlanChange,
    Seats,
    AddOn,
    SubscriptionRenewal,
    WalletTopUp,
}
