using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Stripe;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Infrastructure.Providers.Stripe;

/// <summary>
/// Adapter Stripe de PaymentClient. A diferencia de <c>TaxVision.PaymentApp</c> (un
/// <see cref="StripeClient"/> construido una vez en el constructor desde
/// <c>IOptions&lt;StripeOptions&gt;</c>), acá el mismo <c>Singleton</c> atiende a todos los
/// tenants: cada método recibe <see cref="TenantProviderCredentials"/> ya descifradas y
/// construye su propio <see cref="StripeClient"/> ad-hoc para esa sola llamada. Zero throw
/// hacia el caller: toda <see cref="StripeException"/> se envuelve en <see cref="Result{T}"/>.
/// </summary>
[PaymentProvider(PaymentProviderCode.Stripe)]
public sealed class StripePaymentAdapter(ILogger<StripePaymentAdapter> logger, IStripeClientFactory? clients = null)
    : IPaymentProvider,
        IPaymentWebhookRegistration
{
    private readonly IStripeClientFactory _clients = clients ?? StripeClientFactory.Default;

    public PaymentProviderCode Code => PaymentProviderCode.Stripe;

    private const string PaymentIntentSucceeded = "payment_intent.succeeded";
    private const string PaymentIntentFailed = "payment_intent.payment_failed";
    private const string PaymentIntentCanceled = "payment_intent.canceled";
    private const string ChargeRefunded = "charge.refunded";
    private const string ChargeDisputeCreated = "charge.dispute.created";
    private const string SignatureHeaderName = "Stripe-Signature";

    // Webhook firmado (3DS, reembolsos, disputas): signing secret opcional; sin él lo atascado lo resuelve
    // la revisión periódica. SDK con URL fija.
    public PaymentProviderDescriptor Descriptor { get; } =
        new(
            PaymentProviderCode.Stripe,
            DisplayName: "Stripe",
            PublicKeyLabel: "Publishable key",
            SecretKeyLabel: "Secret key",
            UsesWebhookSecret: true,
            AcceptsApiBaseUrl: false,
            CredentialsHelpUrl: "https://dashboard.stripe.com/apikeys",
            // Los mismos que entiende ParseWebhookEventAsync: la oficina los marca al crear el endpoint.
            WebhookEvents:
            [
                PaymentIntentSucceeded,
                PaymentIntentFailed,
                PaymentIntentCanceled,
                ChargeRefunded,
                ChargeDisputeCreated,
            ],
            WebhookRouteName: PaymentWebhookRoutes.Stripe,
            WebhookSecretLabel: "Signing secret"
        );

    public async Task<Result<ChargeAuthorizationResult>> AuthorizeChargeAsync(
        TenantProviderCredentials credentials,
        ChargeAuthorizationRequest request,
        CancellationToken ct
    )
    {
        var client = _clients.Create(credentials.SecretKey);
        var service = new PaymentIntentService(client);

        try
        {
            var intent = await service.CreateAsync(
                new PaymentIntentCreateOptions
                {
                    Amount = request.Amount.AmountCents,
                    Currency = request.Amount.Currency.ToLowerInvariant(),
                    PaymentMethod = request.PaymentMethod.Token,
                    PaymentMethodTypes = ["card"],
                    Confirm = true,
                    ReceiptEmail = request.ReceiptEmail,
                    StatementDescriptorSuffix = request.Descriptor.Value,
                    Metadata = request.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value),
                    ApplicationFeeAmount = request.ApplicationFee?.AmountCents,
                    // Datos del pagador (charge + payment method) en la misma respuesta; con Connect
                    // se expanden dentro de la cuenta conectada (mismo header Stripe-Account).
                    Expand = [.. StripePayerDetailsReader.ExpandedFields],
                },
                new RequestOptions
                {
                    IdempotencyKey = request.IdempotencyKey.Value,
                    StripeAccount = request.OnBehalfOf,
                },
                ct
            );

            return Result.Success(MapToChargeResult(intent));
        }
        catch (StripeException ex)
        {
            logger.LogWarning(
                ex,
                "Stripe AuthorizeCharge failed. IdempotencyKey={IdempotencyKey}",
                request.IdempotencyKey.Value
            );
            return Result.Success(
                new ChargeAuthorizationResult(
                    ProviderChargeReference: ex.StripeError?.PaymentIntent?.Id ?? string.Empty,
                    Status: PaymentStatus.Failed,
                    FailureCode: ex.StripeError?.Code,
                    FailureMessage: ex.StripeError?.Message ?? ex.Message,
                    Payer: StripePayerDetailsReader.FromError(ex.StripeError)
                )
            );
        }
    }

    private static ChargeAuthorizationResult MapToChargeResult(PaymentIntent intent) =>
        MapChargeStatus(intent) with
        {
            Payer = StripePayerDetailsReader.FromPaymentIntent(intent),
        };

    /// <summary>Resultado del cobro. En <c>requires_action</c> incluye el clientSecret para el 3DS con Stripe.js.</summary>
    public static ChargeAuthorizationResult MapChargeStatus(PaymentIntent intent) =>
        intent.Status switch
        {
            "succeeded" => new ChargeAuthorizationResult(intent.Id, PaymentStatus.Succeeded),
            "requires_action" or "requires_source_action" => new ChargeAuthorizationResult(
                intent.Id,
                PaymentStatus.RequiresAction,
                NextActionType: intent.NextAction?.Type,
                NextActionUrl: intent.NextAction?.RedirectToUrl?.Url,
                NextActionClientSecret: intent.ClientSecret
            ),
            "processing" => new ChargeAuthorizationResult(intent.Id, PaymentStatus.Processing),
            _ => new ChargeAuthorizationResult(
                intent.Id,
                PaymentStatus.Failed,
                FailureCode: intent.Status,
                FailureMessage: intent.LastPaymentError?.Message
            ),
        };

    /// <summary>Retrieve del PaymentIntent guardado como referencia; en Connect, dentro de la cuenta
    /// conectada (header <c>Stripe-Account</c>).</summary>
    public async Task<Result<WebhookEventPayload>> GetChargeStatusAsync(
        TenantProviderCredentials credentials,
        ChargeStatusLookup lookup,
        CancellationToken ct
    )
    {
        var service = new PaymentIntentService(_clients.Create(credentials.SecretKey));
        try
        {
            var intent = await service.GetAsync(
                lookup.ProviderChargeReference,
                new PaymentIntentGetOptions(),
                new RequestOptions { StripeAccount = lookup.OnBehalfOf },
                ct
            );
            return Result.Success(MapIntentStatus(intent));
        }
        catch (StripeException ex)
        {
            logger.LogWarning(
                ex,
                "Stripe PaymentIntent lookup failed. Reference={Reference}",
                lookup.ProviderChargeReference
            );
            return Result.Failure<WebhookEventPayload>(
                new Error("Stripe.ChargeStatus.Failed", ex.StripeError?.Message ?? ex.Message)
            );
        }
    }

    /// <summary>Cancela el PaymentIntent abandonado (en Connect, dentro de la cuenta conectada). Si Stripe lo
    /// rechaza porque el intento cambió de estado (p. ej. se completó en ese momento), se vuelve a consultar
    /// y se devuelve el estado real.</summary>
    public async Task<Result<WebhookEventPayload>> CancelChargeAsync(
        TenantProviderCredentials credentials,
        ChargeStatusLookup lookup,
        CancellationToken ct
    )
    {
        var service = new PaymentIntentService(_clients.Create(credentials.SecretKey));
        try
        {
            var intent = await service.CancelAsync(
                lookup.ProviderChargeReference,
                new PaymentIntentCancelOptions { CancellationReason = "abandoned" },
                new RequestOptions { StripeAccount = lookup.OnBehalfOf },
                ct
            );
            return Result.Success(MapIntentStatus(intent));
        }
        catch (StripeException ex) when (ex.StripeError?.Code == "payment_intent_unexpected_state")
        {
            logger.LogInformation(
                "Stripe refused to cancel PaymentIntent {Reference} ({Message}); reading its current status.",
                lookup.ProviderChargeReference,
                ex.StripeError?.Message
            );
            return await GetChargeStatusAsync(credentials, lookup, ct);
        }
        catch (StripeException ex)
        {
            logger.LogWarning(
                ex,
                "Stripe PaymentIntent cancel failed. Reference={Reference}",
                lookup.ProviderChargeReference
            );
            return Result.Failure<WebhookEventPayload>(
                new Error("Stripe.CancelCharge.Failed", ex.StripeError?.Message ?? ex.Message)
            );
        }
    }

    /// <summary>Estado del PaymentIntent en el payload canónico del webhook. <c>requires_payment_method</c>
    /// solo cuenta como fallo si Stripe dejó el error del intento; si no, sigue pendiente.</summary>
    public static WebhookEventPayload MapIntentStatus(PaymentIntent intent) =>
        intent.Status switch
        {
            "succeeded" => new WebhookEventPayload(
                intent.Id,
                PaymentStatus.Succeeded,
                null,
                null,
                null,
                PaidAmountCents: intent.AmountReceived,
                PaidCurrency: intent.Currency
            ),
            "canceled" => new WebhookEventPayload(
                intent.Id,
                PaymentStatus.Cancelled,
                intent.CancellationReason,
                null,
                null
            ),
            "requires_payment_method" when intent.LastPaymentError is { } error => new WebhookEventPayload(
                intent.Id,
                PaymentStatus.Failed,
                error.Code ?? error.DeclineCode ?? intent.Status,
                error.Message,
                null
            ),
            "requires_action" => new WebhookEventPayload(
                intent.Id,
                PaymentStatus.RequiresAction,
                null,
                null,
                null,
                NextActionClientSecret: intent.ClientSecret
            ),
            _ => new WebhookEventPayload(intent.Id, PaymentStatus.Processing, null, null, null),
        };

    /// <summary>
    /// Crea el webhook endpoint (POST /v1/webhook_endpoints) con los eventos del descriptor y la versión de
    /// API del SDK (la firma la verifica <c>EventUtility</c>, que exige esa versión). El <c>secret</c> solo
    /// viene al crear: si ya había endpoints para nuestra URL no se pueden reutilizar, así que se crea uno
    /// nuevo y después se borran los viejos (best-effort).
    /// </summary>
    public async Task<Result<RegisteredWebhook>> RegisterWebhookAsync(
        TenantProviderCredentials credentials,
        WebhookRegistrationRequest request,
        CancellationToken ct
    )
    {
        var service = new WebhookEndpointService(_clients.Create(credentials.SecretKey));
        var url = request.Url.AbsoluteUri;
        try
        {
            var existing = await service.ListAsync(
                new WebhookEndpointListOptions { Limit = 100 },
                cancellationToken: ct
            );
            var created = await service.CreateAsync(
                new WebhookEndpointCreateOptions
                {
                    Url = url,
                    EnabledEvents = [.. request.Events],
                    ApiVersion = StripeConfiguration.ApiVersion,
                    Description = WebhookDescription,
                },
                cancellationToken: ct
            );
            if (string.IsNullOrEmpty(created.Id) || string.IsNullOrEmpty(created.Secret))
                return Result.Failure<RegisteredWebhook>(
                    new Error("Stripe.Webhook.NoSecret", "Stripe did not return the webhook signing secret.")
                );

            foreach (var stale in existing.Data.Where(e => e.Url == url && e.Id != created.Id))
                await DeleteWebhookAsync(credentials, stale.Id, ct);

            return Result.Success(new RegisteredWebhook(created.Id, created.Secret));
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Stripe webhook registration failed ({Status}).", (int)ex.HttpStatusCode);
            return Result.Failure<RegisteredWebhook>(MapRegistrationError(ex));
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Stripe webhook registration could not reach Stripe.");
            return Result.Failure<RegisteredWebhook>(new Error(WebhookRegistrationErrors.TransientCode, ex.Message));
        }
    }

    /// <summary>DELETE /v1/webhook_endpoints/{id}; uno que ya no existe (404) cuenta como borrado.</summary>
    public async Task<Result> DeleteWebhookAsync(
        TenantProviderCredentials credentials,
        string providerWebhookId,
        CancellationToken ct
    )
    {
        try
        {
            await new WebhookEndpointService(_clients.Create(credentials.SecretKey)).DeleteAsync(
                providerWebhookId,
                cancellationToken: ct
            );
            return Result.Success();
        }
        catch (StripeException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Result.Success();
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Stripe webhook deletion failed ({Status}).", (int)ex.HttpStatusCode);
            return Result.Failure(MapRegistrationError(ex));
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Stripe webhook deletion could not reach Stripe.");
            return Result.Failure(new Error(WebhookRegistrationErrors.TransientCode, ex.Message));
        }
    }

    // Lo ve la oficina en su panel de Stripe junto al endpoint.
    private const string WebhookDescription = "Refunds and disputes sync (created automatically)";

    /// <summary>429/5xx o sin respuesta = pasajero; el resto (clave restringida sin permiso, URL inválida) no.</summary>
    private static Error MapRegistrationError(StripeException ex)
    {
        var status = (int)ex.HttpStatusCode;
        return status is 0 or 429 or >= 500
            ? new Error(WebhookRegistrationErrors.TransientCode, ex.StripeError?.Message ?? ex.Message)
            : new Error($"Stripe.Webhook.{ex.StripeError?.Type ?? "Failed"}", ex.StripeError?.Message ?? ex.Message);
    }

    public async Task<Result<RefundResult>> RefundAsync(
        TenantProviderCredentials credentials,
        string providerChargeReference,
        Money amount,
        string reason,
        CancellationToken ct
    )
    {
        var client = _clients.Create(credentials.SecretKey);
        var service = new RefundService(client);

        try
        {
            var refund = await service.CreateAsync(
                new RefundCreateOptions
                {
                    PaymentIntent = providerChargeReference,
                    Amount = amount.AmountCents,
                    Metadata = new Dictionary<string, string> { ["reason"] = reason },
                },
                cancellationToken: ct
            );

            var status = refund.Status == "succeeded" ? PaymentStatus.Refunded : PaymentStatus.Processing;
            return Result.Success(new RefundResult(refund.Id, status, amount));
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Stripe Refund failed. Reference={Reference}", providerChargeReference);
            return Result.Failure<RefundResult>(
                new Error("Stripe.Refund.Failed", ex.StripeError?.Message ?? ex.Message)
            );
        }
    }

    public Task<Result<WebhookVerificationResult>> VerifyWebhookSignatureAsync(
        string rawPayload,
        IReadOnlyDictionary<string, string> headers,
        TenantProviderCredentials credentials,
        CancellationToken ct
    )
    {
        var signatureHeader = headers.Find(SignatureHeaderName) ?? string.Empty;
        try
        {
            var stripeEvent = EventUtility.ConstructEvent(
                rawPayload,
                signatureHeader,
                credentials.WebhookSecret ?? string.Empty
            );
            return Task.FromResult(
                Result.Success(
                    new WebhookVerificationResult(stripeEvent.Id, stripeEvent.Type, rawPayload, signatureHeader)
                )
            );
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Stripe webhook signature verification failed.");
            // F3: no se ecoa ex.Message al caller (endpoint anónimo) — el detalle queda solo en el log.
            return Task.FromResult(
                Result.Failure<WebhookVerificationResult>(
                    new Error("Stripe.WebhookSignature.Invalid", "The webhook signature could not be verified.")
                )
            );
        }
    }

    public Task<Result<WebhookEventPayload>> ParseWebhookEventAsync(
        string rawPayload,
        string eventType,
        CancellationToken ct
    )
    {
        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ParseEvent(rawPayload);
        }
        catch (Exception ex) when (ex is StripeException or System.Text.Json.JsonException)
        {
            logger.LogWarning(ex, "Stripe webhook payload could not be parsed.");
            return Task.FromResult(
                Result.Failure<WebhookEventPayload>(new Error("Stripe.Webhook.ParseFailed", ex.Message))
            );
        }

        var result = eventType switch
        {
            PaymentIntentSucceeded => ParsePaymentIntent(stripeEvent, PaymentStatus.Succeeded),
            PaymentIntentFailed => ParsePaymentIntent(stripeEvent, PaymentStatus.Failed),
            PaymentIntentCanceled => ParsePaymentIntent(stripeEvent, PaymentStatus.Cancelled),
            ChargeRefunded => ParseCharge(stripeEvent),
            ChargeDisputeCreated => ParseDispute(stripeEvent),
            _ => Result.Failure<WebhookEventPayload>(
                new Error(WebhookEventPayload.UnsupportedEventTypeCode, $"Event type '{eventType}' is not handled.")
            ),
        };

        return Task.FromResult(result);
    }

    private static Result<WebhookEventPayload> ParsePaymentIntent(Event stripeEvent, PaymentStatus mappedStatus)
    {
        if (stripeEvent.Data.Object is not PaymentIntent intent)
            return Result.Failure<WebhookEventPayload>(
                new Error("Stripe.Webhook.UnexpectedPayload", "Expected a PaymentIntent object.")
            );

        return Result.Success(
            new WebhookEventPayload(
                ProviderChargeReference: intent.Id,
                Status: mappedStatus,
                FailureCode: intent.LastPaymentError?.Code,
                FailureMessage: intent.LastPaymentError?.Message,
                RefundedAmountCents: null,
                // Monto cobrado autoritativo solo en el éxito — el handler lo coteja con el cargo (F2).
                PaidAmountCents: mappedStatus == PaymentStatus.Succeeded ? intent.Amount : null,
                PaidCurrency: mappedStatus == PaymentStatus.Succeeded ? intent.Currency : null
            )
        );
    }

    private static Result<WebhookEventPayload> ParseCharge(Event stripeEvent)
    {
        if (stripeEvent.Data.Object is not Charge charge)
            return Result.Failure<WebhookEventPayload>(
                new Error("Stripe.Webhook.UnexpectedPayload", "Expected a Charge object.")
            );

        return Result.Success(
            new WebhookEventPayload(
                ProviderChargeReference: charge.PaymentIntentId ?? charge.Id,
                Status: PaymentStatus.Refunded,
                FailureCode: null,
                FailureMessage: null,
                RefundedAmountCents: charge.AmountRefunded
            )
        );
    }

    private static Result<WebhookEventPayload> ParseDispute(Event stripeEvent)
    {
        if (stripeEvent.Data.Object is not Dispute dispute)
            return Result.Failure<WebhookEventPayload>(
                new Error("Stripe.Webhook.UnexpectedPayload", "Expected a Dispute object.")
            );

        return Result.Success(
            new WebhookEventPayload(
                ProviderChargeReference: dispute.PaymentIntentId ?? string.Empty,
                Status: PaymentStatus.ChargedBack,
                FailureCode: dispute.Reason,
                FailureMessage: "Chargeback dispute created.",
                RefundedAmountCents: null
            )
        );
    }
}
