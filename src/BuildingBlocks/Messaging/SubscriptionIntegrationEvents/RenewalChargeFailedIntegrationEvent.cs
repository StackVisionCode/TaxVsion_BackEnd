namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

/// <summary>
/// Un cobro automático de renovación (plan, asiento o add-on) falló. Lo publica Subscription en cada
/// intento, ya con el nombre de lo que se cobró y lo que pasó después; Auth le pone destinatario y
/// enlace al Account, y Notification manda el correo. Solo cobros automáticos con la tarjeta guardada:
/// un checkout hosteado no lo dispara.
/// </summary>
public sealed record RenewalChargeFailedIntegrationEvent : IntegrationEvent
{
    /// <summary>Motivo con el que viaja por el canal de correos de suscripción.</summary>
    public const string EmailReason = "RenewalChargeFailed";

    public required Guid SaaSPaymentId { get; init; }

    /// <summary><see cref="RenewalChargeKinds"/>.</summary>
    public required string ChargeKind { get; init; }

    /// <summary>Qué se intentó cobrar, en inglés (p. ej. "Professional plan", "1 additional user seat").</summary>
    public required string ItemDescription { get; init; }

    public required int AttemptNumber { get; init; }
    public required int MaxAttempts { get; init; }
    public required bool IsFinal { get; init; }
    public DateTime? NextRetryAtUtc { get; init; }

    public required long AmountCents { get; init; }
    public required string Currency { get; init; }
    public string? CardBrand { get; init; }
    public string? CardLast4 { get; init; }
    public required string FailureCode { get; init; }

    /// <summary>Qué se suspendió tras el último intento; null mientras quedan reintentos. <see cref="RenewalChargeConsequences"/>.</summary>
    public string? Consequence { get; init; }
}
