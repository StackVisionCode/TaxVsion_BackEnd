namespace TaxVision.Referrals.Application.Abstractions;

/// <summary>
/// Solicitud al Wallet de acreditar (o revertir) el premio del referidor, por EVENTO y no por HTTP M2M. Los casos de uso
/// la DEVUELVEN (no publican: la capa Application no conoce el bus) y quien los invoca —el worker o un consumer— la
/// publica con su bus. El Wallet es independiente: no conoce "referidos"; trabaja sobre la referencia opaca
/// (<see cref="ReferenceType"/> + id del caso) y es idempotente por ella, así que repetir la solicitud nunca acredita dos veces.
/// </summary>
public sealed record WalletCreditRequest(
    Guid BeneficiaryTenantId,
    Guid RewardCaseId,
    long AmountCents,
    string Currency,
    string Reason,
    bool Reverse
)
{
    /// <summary>Referencia con la que el Wallet reconoce los créditos de premios de referidos.</summary>
    public const string ReferenceType = "referral-reward";
}
