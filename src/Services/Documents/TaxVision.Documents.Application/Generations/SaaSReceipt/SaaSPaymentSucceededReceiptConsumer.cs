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
                        DescriptionOf(evt.PaymentType),
                        evt.AmountPaidCents,
                        evt.Currency,
                        evt.PaidAtUtc,
                        evt.ProviderReferenceMask,
                        evt.Quantity,
                        evt.UnitAmountCents
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

    /// <summary>El concepto que lee el cliente. Un tipo que aparezca después sale con su nombre crudo, que es
    /// feo pero cierto: mejor eso que un recibo sin concepto.</summary>
    private static string DescriptionOf(string paymentType) =>
        paymentType switch
        {
            // El primer cobro tiene su propio recibo del onboarding (bajo el tenant de plataforma, con el
            // enlace que va por correo), pero ese no se alcanza desde el Account: una vez que el pago pasa a
            // ser del tenant, se le genera el suyo para que el historial pueda ofrecerlo como a los demás.
            "OnboardingInitial" => "First payment",
            "SubscriptionRenewal" or "SubscriptionRenewalCheckout" => "Subscription renewal",
            "SeatRenewal" => "Seat renewal",
            "SeatsPurchaseCharge" => "Extra seats",
            "AddOnRenewal" => "Add-on renewal",
            "AddOnPurchaseCharge" => "Add-on",
            "PlanChangeCharge" or "PlanChangeCheckout" => "Plan upgrade",
            _ => paymentType,
        };
}
