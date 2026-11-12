using BuildingBlocks.Messaging.WalletIntegrationEvents;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Application.Rewards.ClawbackWalletCreditReward;
using TaxVision.Referrals.Application.Rewards.GrantWalletCreditReward;
using Wolverine;

namespace TaxVision.Growth.Api.IntegrationEvents;

/// <summary>
/// El Wallet respondió (<c>wallet.credit.applied.v1</c>) a una solicitud de Growth. Solo reaccionan las respuestas sobre la
/// referencia <c>referral-reward</c>; el resto es de otros servicios y se ignora. El evento viaja con el tenant del
/// referidor, que es el dueño del caso y queda en contexto por el middleware de eventos de integración.
/// <list type="bullet">
/// <item>Crédito aplicado → el caso queda <c>Granted</c> y se registra el beneficio.</item>
/// <item>Reversa aplicada, o «no había crédito que revertir» → el caso queda <c>Reversed</c>.</item>
/// <item>Cualquier otro fallo de negocio → se registra y el caso sigue pendiente: el worker vuelve a pedirlo.</item>
/// </list>
/// </summary>
public static class WalletCreditAppliedConsumer
{
    private const string WalletCreditNotFound = "Wallet.CreditNotFound";
    private static readonly Guid SystemActor = PlatformTenant.Id;

    public static async Task Handle(
        WalletCreditAppliedIntegrationEvent message,
        IMessageBus bus,
        ILogger<WalletCreditAppliedIntegrationEventMarker> logger,
        CancellationToken ct
    )
    {
        if (!string.Equals(message.ReferenceType, WalletCreditRequest.ReferenceType, StringComparison.Ordinal))
            return;

        if (!message.Reverse)
        {
            if (!message.Succeeded || message.LedgerEntryId is not { } ledgerEntryId)
            {
                logger.LogWarning(
                    "Wallet could not credit reward {RewardCaseId}: {ErrorCode}. It stays pending and will be re-requested.",
                    message.ReferenceId,
                    message.ErrorCode
                );
                return;
            }

            var credited = await bus.InvokeAsync<Result<ConfirmWalletCreditRewardOutcome>>(
                new ConfirmWalletCreditRewardCommand(message.TenantId, message.ReferenceId, ledgerEntryId, SystemActor),
                ct
            );
            EnsureApplied(credited);
            return;
        }

        if (!message.Succeeded && message.ErrorCode != WalletCreditNotFound)
        {
            logger.LogWarning(
                "Wallet could not reverse reward {RewardCaseId}: {ErrorCode}. It stays pending and will be re-requested.",
                message.ReferenceId,
                message.ErrorCode
            );
            return;
        }

        var reversed = await bus.InvokeAsync<Result<ConfirmWalletReversalOutcome>>(
            new ConfirmWalletReversalCommand(message.TenantId, message.ReferenceId, SystemActor),
            ct
        );
        EnsureApplied(reversed);
    }

    private static void EnsureApplied<T>(Result<T> result)
    {
        if (result.IsFailure)
            throw GrowthErrorClassification.ToException(result.Error);
    }
}

/// <summary>Marcador solo para tipar el logger del consumer estático.</summary>
public sealed class WalletCreditAppliedIntegrationEventMarker;
