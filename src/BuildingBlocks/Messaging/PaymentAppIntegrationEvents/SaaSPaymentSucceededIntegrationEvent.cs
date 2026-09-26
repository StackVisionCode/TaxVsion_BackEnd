namespace BuildingBlocks.Messaging.PaymentAppIntegrationEvents;

/// <summary>
/// Un cobro SaaS de un tenant REAL quedó confirmado. Es el disparador del recibo: Documents lo consume y
/// genera el PDF. A diferencia de los eventos por tipo (renovación, asientos, add-on, plan…), que le dicen a
/// Subscription QUÉ aprovisionar, éste solo dice que se cobró y cuánto — lo único que un recibo necesita.
/// <para>
/// El primer pago de un onboarding NO viaja por acá: nace sin tenant y ya tiene su propio recibo, pedido por
/// Auth cuando la saga termina (<see cref="OnboardingPaymentSucceededIntegrationEvent"/>).
/// </para>
/// Alias v1.
/// </summary>
public sealed record SaaSPaymentSucceededIntegrationEvent : IntegrationEvent
{
    public required Guid SaaSPaymentId { get; init; }

    /// <summary>Nombre de la oficina a la que se le cobró. Lo pone PaymentApp, que ya tiene la proyección
    /// de tenants: así Documents no necesita una propia solo para el recibo.</summary>
    public required string OfficeName { get; init; }

    /// <summary>Tipo de cobro (<c>SaaSPaymentType</c> como string): de ahí sale el concepto del recibo.</summary>
    public required string PaymentType { get; init; }
    public required long AmountPaidCents { get; init; }
    public required string Currency { get; init; }
    public required DateTime PaidAtUtc { get; init; }

    /// <summary>Últimos caracteres de la referencia del proveedor, nunca la referencia completa.</summary>
    public required string ProviderReferenceMask { get; init; }

    /// <summary>
    /// Cuántas unidades se cobraron y a qué precio, para que el recibo pueda decir "3 × $10.53" y no solo el
    /// total. Ausentes cuando el cobro no tiene unidades que contar; PaymentApp solo las manda si multiplican
    /// exactamente el importe cobrado.
    /// </summary>
    public int? Quantity { get; init; }
    public long? UnitAmountCents { get; init; }
}
