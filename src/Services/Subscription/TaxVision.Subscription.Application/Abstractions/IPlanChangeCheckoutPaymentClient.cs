using BuildingBlocks.Results;

namespace TaxVision.Subscription.Application.Abstractions;

public sealed record PlanChangeCheckoutClientRequest(
    Guid TenantId,
    Guid PlanChangeRequestId,
    long AmountCents,
    string Currency,
    string PayerEmail,
    string SuccessUrl,
    string CancelUrl,
    string IdempotencyKey,
    string Provider,
    string Method,
    // Detalle del cobro y cómo se ve en el historial; opcionales.
    IReadOnlyList<BuildingBlocks.Messaging.PaymentAppIntegrationEvents.SaaSChargeLine>? Lines = null,
    string? ConceptDisplayName = null
);

public sealed record PlanChangeCheckoutClientResult(
    Guid PaymentId,
    string CheckoutUrl,
    string ProviderSessionId,
    DateTime ExpiresAtUtc
);

/// <summary>M2M contra PaymentApp para cobrar un upgrade por redirect. Espejo de
/// <see cref="ISeatCheckoutPaymentClient"/>.</summary>
public interface IPlanChangeCheckoutPaymentClient
{
    Task<Result<PlanChangeCheckoutClientResult>> CreateCheckoutAsync(
        PlanChangeCheckoutClientRequest request,
        CancellationToken ct = default
    );
}
