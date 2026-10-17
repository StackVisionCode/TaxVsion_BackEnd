using BuildingBlocks.Results;
using TaxVision.PaymentApp.Domain.SaaSPayments;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Common;

// Helper fluido para que cada handler que crea un SaaSPayment adjunte un ChargeConcept legible por
// el cliente en el recibo (ConceptDisplayName). Si el concept no se puede construir (bordes raros),
// el payment se devuelve sin concept y el consumer de recibos cae al fallback por PaymentType.
public static class ChargeConceptExtensions
{
    public static Result<SaaSPayment> WithConcept(
        this Result<SaaSPayment> paymentResult,
        string code,
        string displayName
    )
    {
        if (paymentResult.IsFailure)
            return paymentResult;
        var concept = ChargeConcept.Create(code, displayName);
        if (concept.IsSuccess)
            paymentResult.Value.AttachConcept(concept.Value);
        return paymentResult;
    }

    // Resuelve concept por PaymentType cuando el handler no tiene datos específicos (ChargeSaaSPayment,
    // SubscriptionRenewalDueConsumer): cada tipo trae code + label genéricos que lee el recibo.
    // displayName (opcional) viene del caller cross-service cuando el SKU/plan tiene nombre comercial
    // (ej. "+10 GB Storage", "Professional"); si no viene, cae al label genérico por tipo.
    public static Result<SaaSPayment> WithConceptFor(
        this Result<SaaSPayment> paymentResult,
        SaaSPaymentType type,
        int? quantity = null,
        string? displayName = null
    )
    {
        var trimmed = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        var (code, name) = type switch
        {
            SaaSPaymentType.SeatsPurchaseCharge => (
                "seats-purchase",
                quantity is > 0 ? $"+{quantity} seat{(quantity == 1 ? "" : "s")}" : "Extra seats"
            ),
            SaaSPaymentType.AddOnPurchaseCharge => (
                "add-on-purchase",
                trimmed is not null
                    ? (quantity is > 1 ? $"{trimmed} × {quantity}" : trimmed)
                    : (quantity is > 1 ? $"Add-on × {quantity}" : "Add-on")
            ),
            SaaSPaymentType.SubscriptionRenewal => ("subscription-renewal", "Subscription renewal"),
            SaaSPaymentType.SubscriptionRenewalCheckout => ("subscription-renewal", "Subscription renewal"),
            SaaSPaymentType.PlanChangeCheckout => ("plan-upgrade", "Plan upgrade"),
            SaaSPaymentType.OnboardingInitial => ("subscription-signup", trimmed ?? "Subscription signup"),
            _ => (string.Empty, string.Empty),
        };
        return string.IsNullOrEmpty(code) ? paymentResult : paymentResult.WithConcept(code, name);
    }
}
