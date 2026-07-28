namespace BuildingBlocks.Authorization;

/// <summary>Scopes OAuth M2M aceptados por endpoints internos de Billing (audience taxvision-billing).
/// Los scopes M2M son contratos entre servicios y NO deben asignarse a roles humanos (los humanos
/// usan billing.view / billing.manage, ver BillingPermissions).</summary>
public static class BillingServiceScopes
{
    /// <summary>Reprocesar/reconciliar un hecho de pago sobre una factura (operación de recuperación).</summary>
    public const string PaymentReconcile = "billing.payment.reconcile";
}
