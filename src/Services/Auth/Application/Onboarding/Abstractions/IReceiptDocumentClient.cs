using BuildingBlocks.Results;

namespace TaxVision.Auth.Application.Onboarding.Abstractions;

/// <summary>PayFlow (Fase 11) — Auth calling Documents' M2M endpoint to request the onboarding
/// receipt PDF (Fase 10). Fire-and-forget: the caller does not wait for the file, it arrives later
/// via <c>DocumentGenerationCompletedIntegrationEvent</c> (see
/// OnboardingReceiptGenerationCompletedConsumer). Same in-process JWT minting pattern as
/// <see cref="IPaymentAppOnboardingClient"/> — see <c>ReceiptDocumentClient</c> for why.</summary>
public interface IReceiptDocumentClient
{
    Task<Result> RequestReceiptGenerationAsync(RequestReceiptGenerationRequest request, CancellationToken ct = default);
}

/// <summary>Datos del recibo tal cual los conoce Auth al momento del pago. Los montos viajan en
/// centavos + moneda (mismo shape que OnboardingPaymentSucceededIntegrationEvent) —
/// TransactionReferenceMask son los últimos 4 caracteres de la referencia de pago del proveedor,
/// nunca la referencia completa.</summary>
public sealed record RequestReceiptGenerationRequest(
    Guid OnboardingId,
    string PayerFirstName,
    string PayerLastName,
    string PayerEmail,
    string PlanName,
    string PlanCode,
    long PricePaidCents,
    string Currency,
    DateTime PaidAtUtc,
    string TransactionReferenceMask,
    string? PaymentMethodMasked,
    string IdempotencyKey,
    string CorrelationId
);
