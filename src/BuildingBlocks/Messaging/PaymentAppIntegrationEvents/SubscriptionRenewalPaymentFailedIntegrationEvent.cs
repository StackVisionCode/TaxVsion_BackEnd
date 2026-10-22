namespace BuildingBlocks.Messaging.PaymentAppIntegrationEvents;

/// <summary>
/// Publicado por PaymentApp cuando un cobro de renovación de suscripción falla (originado por
/// <see cref="SubscriptionIntegrationEvents.SubscriptionRenewalDueIntegrationEvent"/>).
/// Subscription lo consume para transicionar a PastDue / agendar dunning.
/// </summary>
public sealed record SubscriptionRenewalPaymentFailedIntegrationEvent : IntegrationEvent
{
    public required Guid TenantSubscriptionId { get; init; }
    public required Guid SaaSPaymentId { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string FailureCode { get; init; }
    public required string FailureReason { get; init; }
    public required bool WillRetry { get; init; }
    public DateTime? NextRetryAtUtc { get; init; }

    /// <summary>Número de este intento (1 = primer cobro). 0 en mensajes anteriores a este campo.</summary>
    public int AttemptNumber { get; init; }

    /// <summary>Intentos que tiene el ciclo de cobro: el primero más los reintentos del dunning.</summary>
    public int MaxAttempts { get; init; }

    public long AmountCents { get; init; }
    public string Currency { get; init; } = string.Empty;

    /// <summary>Tarjeta guardada con la que se intentó. Null si no había o no se pudo leer.</summary>
    public string? CardBrand { get; init; }

    public string? CardLast4 { get; init; }

    /// <summary>
    /// Cobro automático con la tarjeta guardada. False en un checkout hosteado: ahí el usuario está en la
    /// pantalla de pago y reintenta él mismo, así que no se le manda correo de cobro fallido.
    /// </summary>
    public bool IsAutomaticCharge { get; init; }
}
