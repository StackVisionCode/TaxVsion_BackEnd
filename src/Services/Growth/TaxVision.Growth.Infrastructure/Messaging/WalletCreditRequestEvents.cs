using BuildingBlocks.Messaging.WalletIntegrationEvents;
using TaxVision.Referrals.Application.Abstractions;

namespace TaxVision.Growth.Infrastructure.Messaging;

/// <summary>
/// Convierte la solicitud que devuelven los casos de uso en el evento de integración genérico hacia el Wallet. Quien
/// invoca el caso de uso (el worker o un consumer) lo publica con su propio bus (sin M2M).
/// </summary>
public static class WalletCreditRequestEvents
{
    private const long MicrosPerCent = 10_000; // 1 USD = 1_000_000 micros = 100 centavos

    public static WalletCreditRequestedIntegrationEvent ToEvent(WalletCreditRequest request) =>
        new()
        {
            TenantId = request.BeneficiaryTenantId,
            ReferenceType = WalletCreditRequest.ReferenceType,
            ReferenceId = request.RewardCaseId,
            AmountMicros = request.AmountCents * MicrosPerCent,
            Currency = request.Currency,
            Reason = request.Reason,
            Reverse = request.Reverse,
        };
}
