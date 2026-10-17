using BuildingBlocks.Common;
using BuildingBlocks.Messaging.DocumentsIntegrationEvents;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Documents.Application.Abstractions;
using Wolverine;

namespace TaxVision.Documents.Application.Generations.SaaSReceipt;

/// <summary>
/// Se confirmó un cobro SaaS: se genera su recibo. El concepto sale del tipo de pago, que es lo único que
/// PaymentApp sabe — Documents no le pregunta a nadie más, porque un recibo no debería depender de que otro
/// servicio esté arriba. El nombre de la oficina viaja en el evento, porque quien lo conoce es PaymentApp.
/// </summary>
public static class SaaSPaymentSucceededReceiptConsumer
{
    public const string TemplateKey = "saas.receipt.v1";
    private const int TemplateVersion = 1;
    private const string SourceService = "payment-app";

    public static async Task Handle(
        SaaSPaymentSucceededIntegrationEvent evt,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<GenerateSaaSReceiptDocumentResult> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            if (evt.TenantId == Guid.Empty)
                return; // El pago de un onboarding tiene su propio recibo, pedido por Auth.

            var result = await bus.InvokeAsync<Result<GenerateSaaSReceiptDocumentResult>>(
                new GenerateSaaSReceiptDocumentCommand(
                    evt.TenantId,
                    evt.SaaSPaymentId,
                    TemplateKey,
                    TemplateVersion,
                    SourceService,
                    // Una clave por pago: la redelivery del evento no genera un segundo recibo.
                    IdempotencyKey: $"saas-receipt:{evt.SaaSPaymentId:N}",
                    correlationId,
                    new SaaSReceiptPayload(
                        evt.OfficeName,
                        !string.IsNullOrWhiteSpace(evt.ConceptDisplayName)
                            ? evt.ConceptDisplayName
                            : DescriptionOf(evt.PaymentType, evt.Quantity ?? 0),
                        evt.AmountPaidCents,
                        evt.Currency,
                        evt.PaidAtUtc,
                        evt.ProviderReferenceMask,
                        evt.Quantity,
                        evt.UnitAmountCents,
                        SecondaryDescriptionOf(evt.PaymentType, evt.Quantity ?? 0)
                    )
                ),
                ct
            );

            if (result.IsFailure)
            {
                logger.LogWarning(
                    "Could not request the receipt for payment {PaymentId}: {Code}.",
                    evt.SaaSPaymentId,
                    result.Error.Code
                );
            }
        }
    }

    /// <summary>El concepto que lee el cliente. 2026-10-16: cuando el PaymentType ya trae Quantity, componer
    /// con ella da información mucho más útil que la etiqueta genérica (comparado contra el bug reportado del
    /// recibo "Add-on × 1" y "Extra seats" sin número). Fix completo con ConceptDisplayName dedicado por SKU
    /// via extensión del integration event queda pendiente para quando se decida tocar el dominio SaaSPayment.</summary>
    private static string DescriptionOf(string paymentType, int quantity) =>
        paymentType switch
        {
            // El primer cobro tiene su propio recibo del onboarding (bajo el tenant de plataforma, con el
            // enlace que va por correo), pero ese no se alcanza desde el Account: una vez que el pago pasa a
            // ser del tenant, se le genera el suyo para que el historial pueda ofrecerlo como a los demás.
            "OnboardingInitial" => "First payment",
            "SubscriptionRenewal" or "SubscriptionRenewalCheckout" => "Subscription renewal",
            "SeatRenewal" => quantity > 1 ? $"{quantity} seat renewals" : "Seat renewal",
            "SeatsPurchaseCharge" => quantity > 0 ? $"+{quantity} seat{(quantity == 1 ? "" : "s")}" : "Extra seats",
            "AddOnRenewal" => "Add-on renewal",
            "AddOnPurchaseCharge" => quantity > 1 ? $"Add-on × {quantity}" : "Add-on",
            "PlanChangeCharge" or "PlanChangeCheckout" => "Plan upgrade",
            "WalletTopUp" => "Wallet top-up",
            _ => paymentType,
        };

    /// <summary>Línea secundaria del recibo — contextualiza qué compró el tenant más allá del "concepto".
    /// Vacía para renovaciones y planes, donde la plantilla ya cae al default "{{ issuer.name }} subscription"
    /// (el pago está asociado a la suscripción como tal).</summary>
    private static string? SecondaryDescriptionOf(string paymentType, int quantity) =>
        paymentType switch
        {
            "SeatsPurchaseCharge" => quantity > 0
                ? $"{quantity} additional seat{(quantity == 1 ? "" : "s")} for your subscription"
                : "Additional seats for your subscription",
            "AddOnPurchaseCharge" => "Add-on for your subscription",
            "WalletTopUp" => "Prepaid wallet credit",
            "PlanChangeCharge" or "PlanChangeCheckout" => "Plan upgrade charge",
            _ => null,
        };
}
