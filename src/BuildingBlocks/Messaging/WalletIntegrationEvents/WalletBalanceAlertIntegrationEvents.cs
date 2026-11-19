using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.WalletIntegrationEvents;

/// <summary>El saldo disponible bajó del umbral de la oficina. Se publica una vez por cruce hacia abajo.</summary>
[MessageIdentity("wallet.low-balance.v1")]
public sealed record WalletLowBalanceIntegrationEvent : IntegrationEvent
{
    public required long AvailableMicros { get; init; }
    public required long ThresholdMicros { get; init; }
    public required string Currency { get; init; }
}

/// <summary>Una recarga automática se cobró y acreditó: sirve de recibo para la oficina.</summary>
[MessageIdentity("wallet.auto-recharge.succeeded.v1")]
public sealed record WalletAutoRechargeSucceededIntegrationEvent : IntegrationEvent
{
    public required Guid TopUpId { get; init; }
    public required long AmountCents { get; init; }
    public required string Currency { get; init; }

    /// <summary>Etiqueta del método cobrado ("Visa •••• 4242", "PayPal").</summary>
    public required string MethodLabel { get; init; }
    public required long AvailableAfterMicros { get; init; }
}

/// <summary>La recarga automática quedó en pausa (cobro fallido o método no disponible). No reintenta sola.</summary>
[MessageIdentity("wallet.auto-recharge.paused.v1")]
public sealed record WalletAutoRechargePausedIntegrationEvent : IntegrationEvent
{
    public Guid? TopUpId { get; init; }

    /// <summary>PaymentFailed | MethodUnavailable.</summary>
    public required string Reason { get; init; }
    public required long AmountCents { get; init; }
    public required string Currency { get; init; }
    public required string MethodLabel { get; init; }
}

/// <summary>Se llegó al tope diario de recargas automáticas. No pausa: al día siguiente de la oficina sigue.</summary>
[MessageIdentity("wallet.auto-recharge.daily-limit.v1")]
public sealed record WalletAutoRechargeDailyLimitReachedIntegrationEvent : IntegrationEvent
{
    public required int DailyLimit { get; init; }
    public required long AmountCents { get; init; }
    public required string Currency { get; init; }
}
