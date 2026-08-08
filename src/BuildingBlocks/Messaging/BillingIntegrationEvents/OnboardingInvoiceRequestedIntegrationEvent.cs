namespace BuildingBlocks.Messaging.BillingIntegrationEvents;

/// <summary>
/// Gift/Referral en onboarding — al completarse la operación comercial (cobro del neto o cobertura 100%
/// por código), Auth (FINALIZE) le pide a Billing que asiente la factura, fuente de verdad financiera.
/// Billing es dueño de la Invoice; Documents solo la renderiza. Se emite en TODOS los casos (pago normal,
/// descuento parcial, cubierta 100% con total $0). Idempotente por <see cref="OnboardingId"/>.
///
/// <see cref="IntegrationEvent.TenantId"/> viaja como <c>PlatformTenant.Id</c> (el tenant real aún no
/// existe); la factura nace bajo ese dueño y se re-hospeda al tenant real cuando la saga lo activa.
/// Regla: solo <c>NetAmountCents &gt; 0</c> lleva <see cref="PaymentId"/> (net = 0 ⇒ sin pago).
/// </summary>
public sealed record OnboardingInvoiceRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid OnboardingId { get; init; }
    public required Guid PlanId { get; init; }

    /// <summary>Descripción de la línea de cargo (nombre del plan) — Billing no resuelve catálogo.</summary>
    public required string PlanDescription { get; init; }

    public required string PayerName { get; init; }
    public string? PayerEmail { get; init; }

    /// <summary>Id del pago real (SaaSPayment) si hubo cobro; null cuando el código cubrió el 100%.</summary>
    public Guid? PaymentId { get; init; }

    public required long GrossAmountCents { get; init; }
    public required long DiscountAmountCents { get; init; }
    public required long NetAmountCents { get; init; }
    public required string Currency { get; init; }

    /// <summary>"Paid" | "Mixed" | "FullyCoveredByCode" (espeja Billing.SettlementType).</summary>
    public required string SettlementType { get; init; }

    /// <summary>Una entrada por beneficio aplicado (referido/promo/gift); la suma = DiscountAmountCents.</summary>
    public IReadOnlyCollection<OnboardingInvoiceAdjustmentDto> Adjustments { get; init; } = [];
}

/// <summary>Línea de ajuste (descuento) para la factura de onboarding. <see cref="AmountCents"/> es la
/// magnitud positiva del descuento. <see cref="Type"/> = "Referral" | "Promo" | "Gift".</summary>
public sealed record OnboardingInvoiceAdjustmentDto(
    string Type,
    string? Code,
    Guid? GrowthReservationId,
    long AmountCents
);
