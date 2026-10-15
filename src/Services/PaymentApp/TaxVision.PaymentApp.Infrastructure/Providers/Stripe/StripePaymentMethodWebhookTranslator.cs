using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using TaxVision.PaymentApp.Application.Abstractions;
using CheckoutSession = Stripe.Checkout.Session;
using CheckoutSessionGetOptions = Stripe.Checkout.SessionGetOptions;
using CheckoutSessionService = Stripe.Checkout.SessionService;
using StripeSetupIntent = Stripe.SetupIntent;

namespace TaxVision.PaymentApp.Infrastructure.Providers.Stripe;

/// <summary>
/// Parsea el evento ya verificado y, cuando corresponde, llama a Stripe para enriquecer con los
/// datos de la tarjeta (brand/last4/exp). Para <c>checkout.session.completed</c> con
/// <c>setup_future_usage=off_session</c> trae el PaymentIntent para obtener su <c>payment_method</c>;
/// luego <c>PaymentMethodService.GetAsync</c> devuelve brand/last4. Para <c>payment_method.attached</c>
/// los datos vienen directamente en el payload.
/// </summary>
public sealed class StripePaymentMethodWebhookTranslator : IStripePaymentMethodWebhookTranslator
{
    private readonly ILogger<StripePaymentMethodWebhookTranslator> _logger;
    private readonly Func<string, CancellationToken, Task<CheckoutSession>> _getSession;
    private readonly Func<string, CancellationToken, Task<PaymentMethod>> _getPaymentMethod;
    private readonly Func<string, CancellationToken, Task<PaymentIntent>> _getPaymentIntent;
    private readonly Func<string, CancellationToken, Task<StripeSetupIntent>> _getSetupIntent;

    public StripePaymentMethodWebhookTranslator(
        IOptions<StripeOptions> options,
        ILogger<StripePaymentMethodWebhookTranslator> logger
    )
    {
        _logger = logger;
        var client = new StripeClient(options.Value.SecretKey);
        var sessionService = new CheckoutSessionService(client);
        var pmService = new PaymentMethodService(client);
        var piService = new PaymentIntentService(client);
        var siService = new SetupIntentService(client);
        _getSession = (id, ct) =>
            sessionService.GetAsync(
                id,
                new CheckoutSessionGetOptions { Expand = ["payment_intent", "setup_intent"] },
                cancellationToken: ct
            );
        _getPaymentMethod = (id, ct) => pmService.GetAsync(id, cancellationToken: ct);
        _getPaymentIntent = (id, ct) => piService.GetAsync(id, cancellationToken: ct);
        _getSetupIntent = (id, ct) => siService.GetAsync(id, cancellationToken: ct);
    }

    // Seam de tests.
    public StripePaymentMethodWebhookTranslator(
        Func<string, CancellationToken, Task<CheckoutSession>> getSession,
        Func<string, CancellationToken, Task<PaymentMethod>> getPaymentMethod,
        Func<string, CancellationToken, Task<PaymentIntent>> getPaymentIntent,
        Func<string, CancellationToken, Task<StripeSetupIntent>> getSetupIntent,
        ILogger<StripePaymentMethodWebhookTranslator> logger
    )
    {
        _getSession = getSession;
        _getPaymentMethod = getPaymentMethod;
        _getPaymentIntent = getPaymentIntent;
        _getSetupIntent = getSetupIntent;
        _logger = logger;
    }

    public async Task<Result<StripePaymentMethodWebhookData?>> TranslateAsync(
        string rawPayload,
        string eventType,
        CancellationToken ct
    )
    {
        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ParseEvent(rawPayload, throwOnApiVersionMismatch: false);
        }
        catch (Exception ex) when (ex is StripeException or System.Text.Json.JsonException)
        {
            _logger.LogWarning(ex, "Stripe payment-method webhook payload could not be parsed.");
            return Result.Failure<StripePaymentMethodWebhookData?>(
                new Error("Stripe.PmWebhook.ParseFailed", ex.Message)
            );
        }

        try
        {
            return eventType switch
            {
                "checkout.session.completed" => await TranslateCheckoutSessionAsync(stripeEvent, ct),
                "payment_method.attached" => TranslatePaymentMethodAttached(stripeEvent),
                _ => Result.Success<StripePaymentMethodWebhookData?>(null),
            };
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe payment-method webhook enrichment failed. Event={EventType}", eventType);
            return Result.Failure<StripePaymentMethodWebhookData?>(
                new Error("Stripe.PmWebhook.EnrichmentFailed", ex.StripeError?.Message ?? ex.Message)
            );
        }
    }

    private async Task<Result<StripePaymentMethodWebhookData?>> TranslateCheckoutSessionAsync(
        Event stripeEvent,
        CancellationToken ct
    )
    {
        if (stripeEvent.Data.Object is not CheckoutSession session)
            return Result.Success<StripePaymentMethodWebhookData?>(null);

        if (string.IsNullOrEmpty(session.CustomerId))
            return Result.Success<StripePaymentMethodWebhookData?>(null);

        // Setup-only session (flujo "Update card"): no hay payment_intent, el método vive en setup_intent.
        if (string.Equals(session.Mode, "setup", StringComparison.Ordinal))
            return await TranslateSetupSessionAsync(session, ct);

        // Re-fetch para asegurarnos de tener el payment_intent expandido con su payment_method actual —
        // el payload inicial del webhook a veces lo trae null incluso cuando el PI ya existe.
        var fullSession = await _getSession(session.Id, ct);
        var pi = fullSession.PaymentIntent;
        if (pi is null && !string.IsNullOrEmpty(fullSession.PaymentIntentId))
            pi = await _getPaymentIntent(fullSession.PaymentIntentId, ct);
        if (pi is null)
            return Result.Success<StripePaymentMethodWebhookData?>(null);

        // Solo nos interesan sesiones que pidieron guardado para off-session; el resto son one-shot
        // y no deben tocar TenantProviderCustomer.
        if (!string.Equals(pi.SetupFutureUsage, "off_session", StringComparison.Ordinal))
            return Result.Success<StripePaymentMethodWebhookData?>(null);

        if (string.IsNullOrEmpty(pi.PaymentMethodId))
            return Result.Success<StripePaymentMethodWebhookData?>(null);

        var pm = await _getPaymentMethod(pi.PaymentMethodId, ct);
        if (pm.Card is null)
            return Result.Success<StripePaymentMethodWebhookData?>(null);

        var tenantId = ExtractTenantId(fullSession.Metadata);
        var email = fullSession.CustomerEmail ?? pm.BillingDetails?.Email ?? string.Empty;

        return Result.Success<StripePaymentMethodWebhookData?>(
            new StripePaymentMethodWebhookData(
                TenantId: tenantId,
                StripeCustomerId: fullSession.CustomerId,
                PayerEmail: email,
                MethodReference: pm.Id,
                Brand: pm.Card.Brand,
                Last4: pm.Card.Last4,
                ExpMonth: (int)pm.Card.ExpMonth,
                ExpYear: (int)pm.Card.ExpYear
            )
        );
    }

    private static Result<StripePaymentMethodWebhookData?> TranslatePaymentMethodAttached(Event stripeEvent)
    {
        if (stripeEvent.Data.Object is not PaymentMethod pm || pm.Card is null)
            return Result.Success<StripePaymentMethodWebhookData?>(null);

        if (string.IsNullOrEmpty(pm.CustomerId))
            return Result.Success<StripePaymentMethodWebhookData?>(null);

        var tenantId = ExtractTenantId(pm.Metadata);
        var email = pm.BillingDetails?.Email ?? string.Empty;

        return Result.Success<StripePaymentMethodWebhookData?>(
            new StripePaymentMethodWebhookData(
                TenantId: tenantId,
                StripeCustomerId: pm.CustomerId,
                PayerEmail: email,
                MethodReference: pm.Id,
                Brand: pm.Card.Brand,
                Last4: pm.Card.Last4,
                ExpMonth: (int)pm.Card.ExpMonth,
                ExpYear: (int)pm.Card.ExpYear
            )
        );
    }

    /// <summary>Setup-only session: no cobra. Trae el <c>setup_intent</c> (expandido o por id) para leer
    /// el <c>payment_method</c> capturado, luego <c>PaymentMethodService.GetAsync</c> para brand/last4/exp.</summary>
    private async Task<Result<StripePaymentMethodWebhookData?>> TranslateSetupSessionAsync(
        CheckoutSession session,
        CancellationToken ct
    )
    {
        var fullSession = await _getSession(session.Id, ct);
        var setupIntent = fullSession.SetupIntent;
        if (setupIntent is null && !string.IsNullOrEmpty(fullSession.SetupIntentId))
            setupIntent = await _getSetupIntent(fullSession.SetupIntentId, ct);
        if (setupIntent is null || string.IsNullOrEmpty(setupIntent.PaymentMethodId))
            return Result.Success<StripePaymentMethodWebhookData?>(null);

        var pm = await _getPaymentMethod(setupIntent.PaymentMethodId, ct);
        if (pm.Card is null)
            return Result.Success<StripePaymentMethodWebhookData?>(null);

        var tenantId = ExtractTenantId(fullSession.Metadata) ?? ExtractTenantId(setupIntent.Metadata);
        var email = fullSession.CustomerEmail ?? pm.BillingDetails?.Email ?? string.Empty;

        return Result.Success<StripePaymentMethodWebhookData?>(
            new StripePaymentMethodWebhookData(
                TenantId: tenantId,
                StripeCustomerId: fullSession.CustomerId,
                PayerEmail: email,
                MethodReference: pm.Id,
                Brand: pm.Card.Brand,
                Last4: pm.Card.Last4,
                ExpMonth: (int)pm.Card.ExpMonth,
                ExpYear: (int)pm.Card.ExpYear,
                IsSetupMode: true
            )
        );
    }

    private static Guid? ExtractTenantId(IDictionary<string, string>? metadata)
    {
        if (metadata is null || !metadata.TryGetValue("tenantId", out var value) || string.IsNullOrWhiteSpace(value))
            return null;
        return Guid.TryParseExact(value, "N", out var parsed)
            ? parsed
            : (Guid.TryParse(value, out var parsed2) ? parsed2 : (Guid?)null);
    }
}
