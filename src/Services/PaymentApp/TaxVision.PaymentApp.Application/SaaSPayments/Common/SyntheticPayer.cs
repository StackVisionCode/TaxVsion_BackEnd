namespace TaxVision.PaymentApp.Application.SaaSPayments.Common;

/// <summary>
/// Fase A/B/C no tienen todavía <c>TenantProviderCustomer</c> (Fase D) ni el email real del
/// admin del tenant — se usa un email sintético determinístico para registrar el customer en
/// el provider. Cuando Fase D exista, este helper se reemplaza por la resolución real.
/// </summary>
public static class SyntheticPayer
{
    public static string EmailFor(Guid tenantId) => $"tenant-{tenantId:N}@payments.taxvision.internal";
}
