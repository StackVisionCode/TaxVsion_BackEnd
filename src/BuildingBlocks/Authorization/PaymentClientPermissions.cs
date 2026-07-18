namespace BuildingBlocks.Authorization;

/// <summary>
/// Permisos del microservicio PaymentClient. Mismo patrón que <see cref="PaymentAppPermissions"/>:
/// claves punteadas en minúsculas usadas como claim "perm" en el JWT y como policy en los
/// endpoints via <c>[HasPermission(PaymentClientPermissions.ConfigManage)]</c>.
/// </summary>
public static class PaymentClientPermissions
{
    public const string ConfigRead = "payment_client.config.read";
    public const string ConfigManage = "payment_client.config.manage";
    public const string PaymentRead = "payment_client.payment.read";
    public const string PaymentCharge = "payment_client.payment.charge";
    public const string PaymentRefund = "payment_client.payment.refund";
    public const string PaymentLinkRead = "payment_client.payment_link.read";
    public const string PaymentLinkManage = "payment_client.payment_link.manage";
    public const string ConnectAccountRead = "payment_client.connect_account.read";
    public const string ConnectAccountOnboard = "payment_client.connect_account.onboard";
    public const string PayoutRead = "payment_client.payout.read";
    public const string PayoutManage = "payment_client.payout.manage";
    public const string RecurringRead = "payment_client.recurring.read";
    public const string RecurringManage = "payment_client.recurring.manage";

    /// <summary>Ve pagos de CUALQUIER tenant, incluso suspendido — solo para investigación/
    /// soporte (§42.6 del diseño, análogo a <see cref="PaymentAppPermissions.AdminCrossTenant"/>).</summary>
    public const string AdminCrossTenant = "payment_client.admin.cross_tenant";
}
