using BuildingBlocks.Results;

namespace TaxVision.PaymentApp.Application.Abstractions;

/// <summary>
/// Traduce un evento de webhook de Stripe (<c>checkout.session.completed</c> con
/// <c>setup_future_usage=off_session</c>, o <c>payment_method.attached</c>) al dato canónico que
/// el handler de Application usa para persistir el método de pago. Devuelve null cuando el
/// evento no acarrea un método adjuntable (p.ej. checkout one-shot sin off-session). La llamada
/// a <c>PaymentMethodService.GetAsync</c> ocurre adentro — por eso vive en Infrastructure.
/// </summary>
public interface IStripePaymentMethodWebhookTranslator
{
    Task<Result<StripePaymentMethodWebhookData?>> TranslateAsync(
        string rawPayload,
        string eventType,
        CancellationToken ct
    );
}

/// <summary>
/// Datos autoritativos del método tokenizado, directo del SDK de Stripe. <paramref name="TenantId"/>
/// se extrae de la metadata del evento cuando está — sin ella el handler no puede atribuir el
/// método a un tenant y acknowledgea sin persistir. <paramref name="IsSetupMode"/> es true cuando
/// el evento vino de una Checkout Session <c>mode=setup</c> (flujo "Update card" sin cobro) — el
/// handler reemplaza el default existente en vez de solo marcar al nuevo como default.
/// </summary>
public sealed record StripePaymentMethodWebhookData(
    Guid? TenantId,
    string StripeCustomerId,
    string PayerEmail,
    string MethodReference,
    string Brand,
    string Last4,
    int ExpMonth,
    int ExpYear,
    bool IsSetupMode = false
);
