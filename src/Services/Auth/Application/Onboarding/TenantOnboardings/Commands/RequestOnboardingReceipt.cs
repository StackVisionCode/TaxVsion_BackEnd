using Microsoft.Extensions.Logging;
using TaxVision.Auth.Application.Onboarding.Abstractions;

namespace TaxVision.Auth.Application.Onboarding.TenantOnboardings.Commands;

/// <summary>
/// Comando local (Wolverine) que le pide a Documents generar el PDF del RECIBO de pago del comprador —
/// cortesía al pagador, distinto de la factura financiera que asienta Billing. Auto-contenido: lleva los
/// datos del pago tomados del onboarding EN MEMORIA por el caller (mismo motivo que
/// <see cref="OnboardingFinalizeCommand"/>). El PDF llega después, asíncrono, vía
/// <c>DocumentGenerationCompletedIntegrationEvent</c> (lo cierra OnboardingReceiptGenerationCompletedConsumer).
/// Idempotente en Documents por <c>Idempotency-Key</c>: un reintento no duplica el recibo.
/// </summary>
public sealed record RequestOnboardingReceiptCommand(
    Guid OnboardingId,
    string PayerFirstName,
    string PayerLastName,
    string PayerEmail,
    string PlanName,
    long PricePaidCents,
    string Currency,
    DateTime PaidAtUtc,
    // Referencia cruda del proveedor de pago; el handler la enmascara (últimos 4). Null en el carril $0.
    string? ProviderPaymentReference,
    string? PaymentMethodMasked,
    string CorrelationId
);

public static class RequestOnboardingReceiptHandler
{
    public static async Task Handle(
        RequestOnboardingReceiptCommand command,
        IReceiptDocumentClient receipts,
        ILogger<RequestOnboardingReceiptCommand> logger,
        CancellationToken ct
    )
    {
        // Últimos 4 caracteres de la referencia del proveedor (nunca la referencia completa); vacío en $0.
        var reference = command.ProviderPaymentReference ?? string.Empty;
        var mask = reference.Length <= 4 ? reference : reference[^4..];

        var result = await receipts.RequestReceiptGenerationAsync(
            new RequestReceiptGenerationRequest(
                command.OnboardingId,
                command.PayerFirstName,
                command.PayerLastName,
                command.PayerEmail,
                command.PlanName,
                command.PricePaidCents,
                command.Currency,
                command.PaidAtUtc,
                mask,
                command.PaymentMethodMasked,
                IdempotencyKey: $"onb-receipt:{command.OnboardingId:N}",
                command.CorrelationId
            ),
            ct
        );

        // Fallo transitorio (Documents caído) → lanza para que Wolverine reintente; la generación
        // deduplica por Idempotency-Key, así que reintentar no produce un segundo recibo.
        if (result.IsFailure)
            throw new InvalidOperationException(
                $"Onboarding receipt request failed for {command.OnboardingId}: {result.Error.Code} - {result.Error.Message}"
            );

        logger.LogInformation(
            "Onboarding {OnboardingId} receipt generation requested to Documents.",
            command.OnboardingId
        );
    }
}
