using BuildingBlocks.Messaging.WalletIntegrationEvents;
using TaxVision.Wallet.Domain.Wallet;
using TaxVision.Wallet.Domain.Wallet.Events;
using Wolverine;

namespace TaxVision.Wallet.Application.Wallet.DomainEvents;

/// <summary>
/// El saldo cambió: devuelve el aviso de integración (cascada por el outbox) para que el front refresque.
/// Corre después del commit, desde la cola domain-events. Todo sale del evento; no relee el monedero.
/// </summary>
public static class WalletBalanceChangedDomainEventHandler
{
    public static WalletBalanceChangedIntegrationEvent Handle(
        WalletBalanceChangedDomainEvent changed,
        Envelope envelope
    ) =>
        new()
        {
            TenantId = changed.TenantId,
            OccurredOn = changed.OccurredAtUtc,
            Reason = ReasonFor(changed.Movement),
            CorrelationId = envelope.CorrelationId ?? string.Empty,
        };

    private static string ReasonFor(MovementType movement) =>
        movement switch
        {
            MovementType.TopUp => "topup",
            MovementType.Reserve => "reserve",
            MovementType.Consume => "settle",
            MovementType.Release => "release",
            MovementType.UsageRefund => "refund",
            MovementType.Adjustment => "adjust",
            MovementType.Reward => "reward",
            MovementType.RewardReversal => "reward_reversal",
            _ => movement.ToString().ToLowerInvariant(),
        };
}
