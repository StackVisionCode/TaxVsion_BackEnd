namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

/// <summary>
/// Publicado por Subscription cada vez que un tenant elige o cambia su preferencia de cobro
/// (Automatic/Manual) o acepta un nuevo mandate por cambio de precio. PaymentApp lo
/// consume para mantener una proyección local, de forma que el consumer del evento de
/// renovación decida sin ir contra Subscription por M2M en cada cobro.
/// </summary>
public sealed record TenantBillingPreferenceChangedIntegrationEvent : IntegrationEvent
{
    /// <summary>"Automatic" o "Manual" — convención stringificada del enum del dominio para
    /// no acoplar BuildingBlocks al enum concreto.</summary>
    public required string Mode { get; init; }

    /// <summary>"Stripe"/"PayPal"/... cuando el modo es Automatic. Null en Manual.</summary>
    public string? ProviderCode { get; init; }

    /// <summary>True solo cuando hay un mandate no revocado en el momento del cambio.
    /// El consumer en PaymentApp usa esto como gate para permitir el cobro off-session.</summary>
    public required bool HasActiveMandate { get; init; }

    /// <summary>Monto/moneda/cadencia pactados en el mandate activo (evidencia legal). Null si
    /// no hay mandate activo (ej. Manual).</summary>
    public long? AgreedAmountCents { get; init; }
    public string? AgreedCurrency { get; init; }
    public string? AgreedCadence { get; init; }

    /// <summary>Monotónico — el proyector descarta eventos fuera de orden comparando contra el
    /// valor guardado. Fuente: UpdatedAtUtc del aggregate en ticks UTC.</summary>
    public required long RevisionTicks { get; init; }

    public required DateTime ChangedAtUtc { get; init; }
}
