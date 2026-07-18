namespace BuildingBlocks.Authorization;

/// <summary>
/// Permisos del microservicio PaymentApp. Mismo patrón que <see cref="SignaturePermissions"/>:
/// claves punteadas en minúsculas usadas como claim "perm" en el JWT y como policy en los
/// endpoints via <c>[HasPermission(PaymentAppPermissions.SaaSPaymentRead)]</c>.
/// </summary>
public static class PaymentAppPermissions
{
    public const string SaaSPaymentRead = "payment_app.saas_payment.read";
    public const string SaaSPaymentRefund = "payment_app.saas_payment.refund";
    public const string ProviderCustomerRead = "payment_app.provider_customer.read";
    public const string ProviderCustomerManage = "payment_app.provider_customer.manage";

    /// <summary>Ve pagos de CUALQUIER tenant, incluso suspendido — solo para investigación/
    /// soporte (§42.6 del diseño). Deliberadamente separado de <see cref="SaaSPaymentRead"/>
    /// (que ya está scoped al propio tenant vía JWT).</summary>
    public const string AdminCrossTenant = "payment_app.admin.cross_tenant";
}
