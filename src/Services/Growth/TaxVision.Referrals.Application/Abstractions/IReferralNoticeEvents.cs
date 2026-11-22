using TaxVision.Referrals.Application.Attributions.CreateReferralAttribution;
using TaxVision.Referrals.Application.Rewards;

namespace TaxVision.Referrals.Application.Abstractions;

/// <summary>
/// Arma los avisos al referidor sin que Application conozca el bus. El caso de uso los devuelve y Wolverine los
/// publica por el outbox del mismo mensaje: el aviso se confirma en la misma transacción que el cambio de estado.
/// </summary>
public interface IReferralNoticeEvents
{
    object Attributed(CreateReferralAttributionResult attribution, string correlationId);

    object Qualified(ReferralRewardNotice reward, Guid paymentId, string correlationId);

    object Rejected(ReferralRewardNotice reward, string reasonCode, string correlationId);
}
