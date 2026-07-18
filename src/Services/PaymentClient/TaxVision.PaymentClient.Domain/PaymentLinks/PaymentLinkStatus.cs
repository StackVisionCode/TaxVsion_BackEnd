namespace TaxVision.PaymentClient.Domain.PaymentLinks;

/// <summary>
/// Active ──used──▶ Used
/// Active ──expired──▶ Expired
/// Active ──revoked by admin──▶ Revoked
/// Todos los estados terminales son finales — un link nunca vuelve a Active.
/// </summary>
public enum PaymentLinkStatus
{
    Active = 1,
    Used = 2,
    Expired = 3,
    Revoked = 4,
}
