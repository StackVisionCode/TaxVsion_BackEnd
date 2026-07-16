using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.ValueObjects;
using Stripe;

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
public sealed class StripePaymentAdapter(ILogger<StripePaymentAdapter> logger) : IPaymentProvider
{
    public PaymentProviderCode Code => PaymentProviderCode.Stripe;

    public async Task<Result<ChargeAuthorizationResult>> AuthorizeChargeAsync(
        TenantProviderCredentials credentials, ChargeAuthorizationRequest request, CancellationToken ct)
    {
        var client = new StripeClient(credentials.SecretKey);
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
                    StatementDescriptor = request.Descriptor.Value,
                    Metadata = request.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value),
                    ApplicationFeeAmount = request.ApplicationFee?.AmountCents,
                },
                new RequestOptions { IdempotencyKey = request.IdempotencyKey.Value, StripeAccount = request.OnBehalfOf },
                ct);

            return Result.Success(MapToChargeResult(intent));
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Stripe AuthorizeCharge failed. IdempotencyKey={IdempotencyKey}", request.IdempotencyKey.Value);
            return Result.Success(new ChargeAuthorizationResult(
                ProviderChargeReference: ex.StripeError?.PaymentIntent?.Id ?? string.Empty,
                Status: PaymentStatus.Failed,
                FailureCode: ex.StripeError?.Code,
                FailureMessage: ex.StripeError?.Message ?? ex.Message));
        }
    }

    private static ChargeAuthorizationResult MapToChargeResult(PaymentIntent intent) => intent.Status switch
    {
        "succeeded" => new ChargeAuthorizationResult(intent.Id, PaymentStatus.Succeeded),
        "requires_action" or "requires_source_action" => new ChargeAuthorizationResult(
            intent.Id, PaymentStatus.RequiresAction,
            NextActionType: intent.NextAction?.Type,
            NextActionUrl: intent.NextAction?.RedirectToUrl?.Url),
        "processing" => new ChargeAuthorizationResult(intent.Id, PaymentStatus.Processing),
        _ => new ChargeAuthorizationResult(
            intent.Id, PaymentStatus.Failed,
            FailureCode: intent.Status,
            FailureMessage: intent.LastPaymentError?.Message),
    };

    public async Task<Result<RefundResult>> RefundAsync(
        TenantProviderCredentials credentials, string providerChargeReference, Money amount, string reason, CancellationToken ct)
    {
        var client = new StripeClient(credentials.SecretKey);
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
                cancellationToken: ct);

            var status = refund.Status == "succeeded" ? PaymentStatus.Refunded : PaymentStatus.Processing;
            return Result.Success(new RefundResult(refund.Id, status, amount));
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Stripe Refund failed. Reference={Reference}", providerChargeReference);
            return Result.Failure<RefundResult>(new Error("Stripe.Refund.Failed", ex.StripeError?.Message ?? ex.Message));
        }
    }

    public Task<Result<WebhookVerificationResult>> VerifyWebhookSignatureAsync(
        string rawPayload, string signatureHeader, string webhookSecret, CancellationToken ct)
    {
        try
        {
            var stripeEvent = EventUtility.ConstructEvent(rawPayload, signatureHeader, webhookSecret);
            return Task.FromResult(Result.Success(new WebhookVerificationResult(stripeEvent.Id, stripeEvent.Type, rawPayload)));
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Stripe webhook signature verification failed.");
            return Task.FromResult(Result.Failure<WebhookVerificationResult>(new Error("Stripe.WebhookSignature.Invalid", ex.Message)));
        }
    }

    public Task<Result<WebhookEventPayload>> ParseWebhookEventAsync(string rawPayload, string eventType, CancellationToken ct)
    {
        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ParseEvent(rawPayload);
        }
        catch (Exception ex) when (ex is StripeException or System.Text.Json.JsonException)
        {
            logger.LogWarning(ex, "Stripe webhook payload could not be parsed.");
            return Task.FromResult(Result.Failure<WebhookEventPayload>(new Error("Stripe.Webhook.ParseFailed", ex.Message)));
        }

        var result = eventType switch
        {
            "payment_intent.succeeded" => ParsePaymentIntent(stripeEvent, PaymentStatus.Succeeded),
            "payment_intent.payment_failed" => ParsePaymentIntent(stripeEvent, PaymentStatus.Failed),
            "payment_intent.canceled" => ParsePaymentIntent(stripeEvent, PaymentStatus.Cancelled),
            "charge.refunded" => ParseCharge(stripeEvent),
            "charge.dispute.created" => ParseDispute(stripeEvent),
            _ => Result.Failure<WebhookEventPayload>(new Error("Stripe.Webhook.UnsupportedEventType", $"Event type '{eventType}' is not handled.")),
        };

        return Task.FromResult(result);
    }

    private static Result<WebhookEventPayload> ParsePaymentIntent(Event stripeEvent, PaymentStatus mappedStatus)
    {
        if (stripeEvent.Data.Object is not PaymentIntent intent)
            return Result.Failure<WebhookEventPayload>(new Error("Stripe.Webhook.UnexpectedPayload", "Expected a PaymentIntent object."));

        return Result.Success(new WebhookEventPayload(
            ProviderChargeReference: intent.Id,
            Status: mappedStatus,
            FailureCode: intent.LastPaymentError?.Code,
            FailureMessage: intent.LastPaymentError?.Message,
            RefundedAmountCents: null));
    }

    private static Result<WebhookEventPayload> ParseCharge(Event stripeEvent)
    {
        if (stripeEvent.Data.Object is not Charge charge)
            return Result.Failure<WebhookEventPayload>(new Error("Stripe.Webhook.UnexpectedPayload", "Expected a Charge object."));

        return Result.Success(new WebhookEventPayload(
            ProviderChargeReference: charge.PaymentIntentId ?? charge.Id,
            Status: PaymentStatus.Refunded,
            FailureCode: null,
            FailureMessage: null,
            RefundedAmountCents: charge.AmountRefunded));
    }

    private static Result<WebhookEventPayload> ParseDispute(Event stripeEvent)
    {
        if (stripeEvent.Data.Object is not Dispute dispute)
            return Result.Failure<WebhookEventPayload>(new Error("Stripe.Webhook.UnexpectedPayload", "Expected a Dispute object."));

        return Result.Success(new WebhookEventPayload(
            ProviderChargeReference: dispute.PaymentIntentId ?? string.Empty,
            Status: PaymentStatus.ChargedBack,
            FailureCode: dispute.Reason,
            FailureMessage: "Chargeback dispute created.",
            RefundedAmountCents: null));
    }
}
