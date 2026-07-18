using System.Diagnostics;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Infrastructure.Providers.Stripe;

/// <summary>
/// Provider primario. Usa <c>Stripe.net</c> contra <see cref="PaymentIntentService"/> /
/// <see cref="CustomerService"/> / <see cref="RefundService"/>. Zero throw hacia el caller:
/// toda <see cref="StripeException"/> se envuelve en <see cref="Result{T}"/>.
/// </summary>
[PaymentProvider(PaymentProviderCode.Stripe)]
public sealed class StripePaymentAdapter : IPaymentProvider
{
    private readonly StripeClient _client;
    private readonly ILogger<StripePaymentAdapter> _logger;
    private readonly IPaymentAppMetrics _metrics;

    public StripePaymentAdapter(
        IOptions<StripeOptions> options,
        ILogger<StripePaymentAdapter> logger,
        IPaymentAppMetrics metrics
    )
    {
        _client = new StripeClient(options.Value.SecretKey);
        _logger = logger;
        _metrics = metrics;
    }

    public PaymentProviderCode Code => PaymentProviderCode.Stripe;
    public ProviderCapabilities Capabilities => StripeCapabilities.Instance;

    public async Task<Result<ProviderCustomerToken>> GetOrCreateCustomerAsync(
        Guid tenantId,
        string email,
        string? name,
        CancellationToken ct
    )
    {
        var service = new CustomerService(_client);
        try
        {
            var search = await service.SearchAsync(
                new CustomerSearchOptions { Query = $"metadata['tenantId']:'{tenantId:N}'", Limit = 1 },
                cancellationToken: ct
            );

            var existing = search.Data.Count > 0 ? search.Data[0] : null;
            if (existing is not null)
                return Result.Success(new ProviderCustomerToken(existing.Id, PaymentProviderCode.Stripe));

            var created = await service.CreateAsync(
                new CustomerCreateOptions
                {
                    Email = email,
                    Name = name,
                    Metadata = new Dictionary<string, string> { ["tenantId"] = tenantId.ToString("N") },
                },
                cancellationToken: ct
            );

            return Result.Success(new ProviderCustomerToken(created.Id, PaymentProviderCode.Stripe));
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe GetOrCreateCustomer failed for tenant {TenantId}", tenantId);
            return Result.Failure<ProviderCustomerToken>(
                new Error("Stripe.Customer.Failed", ex.StripeError?.Message ?? ex.Message)
            );
        }
    }

    public async Task<Result<SavedPaymentMethodInfo>> AttachPaymentMethodAsync(
        ProviderCustomerToken customer,
        string paymentMethodReference,
        CancellationToken ct
    )
    {
        var service = new PaymentMethodService(_client);
        try
        {
            var method = await service.AttachAsync(
                paymentMethodReference,
                new PaymentMethodAttachOptions { Customer = customer.Token },
                cancellationToken: ct
            );

            if (method.Card is null)
                return Result.Failure<SavedPaymentMethodInfo>(
                    new Error("Stripe.PaymentMethod.NotACard", "Only card payment methods are supported.")
                );

            return Result.Success(
                new SavedPaymentMethodInfo(
                    MethodReference: method.Id,
                    Brand: method.Card.Brand,
                    Last4: method.Card.Last4,
                    ExpMonth: (int)method.Card.ExpMonth,
                    ExpYear: (int)method.Card.ExpYear
                )
            );
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe AttachPaymentMethod failed. Reference={Reference}", paymentMethodReference);
            return Result.Failure<SavedPaymentMethodInfo>(
                new Error("Stripe.PaymentMethod.AttachFailed", ex.StripeError?.Message ?? ex.Message)
            );
        }
    }

    public async Task<Result> DetachPaymentMethodAsync(string paymentMethodReference, CancellationToken ct)
    {
        var service = new PaymentMethodService(_client);
        try
        {
            await service.DetachAsync(paymentMethodReference, cancellationToken: ct);
            return Result.Success();
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe DetachPaymentMethod failed. Reference={Reference}", paymentMethodReference);
            return Result.Failure(
                new Error("Stripe.PaymentMethod.DetachFailed", ex.StripeError?.Message ?? ex.Message)
            );
        }
    }

    public async Task<Result<ChargeAuthorizationResult>> AuthorizeChargeAsync(
        ChargeAuthorizationRequest request,
        CancellationToken ct
    )
    {
        var service = new PaymentIntentService(_client);
        var hasPaymentMethod = request.SpecificPaymentMethod is not null;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var intent = await service.CreateAsync(
                new PaymentIntentCreateOptions
                {
                    Amount = request.Amount.AmountCents,
                    Currency = request.Amount.Currency.ToLowerInvariant(),
                    Customer = request.Customer.Token,
                    PaymentMethod = request.SpecificPaymentMethod?.Token,
                    PaymentMethodTypes = hasPaymentMethod ? ["card"] : null,
                    Confirm = hasPaymentMethod,
                    OffSession = hasPaymentMethod,
                    // Stripe rechaza StatementDescriptor para payment_method_type "card" —
                    // pide StatementDescriptorSuffix en su lugar (se concatena al descriptor
                    // ya configurado en el dashboard de la cuenta Stripe).
                    StatementDescriptorSuffix = request.Descriptor.Value,
                    Metadata = request.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value),
                },
                new RequestOptions { IdempotencyKey = request.IdempotencyKey.Value },
                ct
            );

            return Result.Success(MapToChargeResult(intent));
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(
                ex,
                "Stripe AuthorizeCharge failed. IdempotencyKey={IdempotencyKey}",
                request.IdempotencyKey.Value
            );
            return Result.Success(
                new ChargeAuthorizationResult(
                    ProviderChargeReference: ex.StripeError?.PaymentIntent?.Id ?? string.Empty,
                    Status: PaymentStatus.Failed,
                    FailureCode: ex.StripeError?.Code,
                    FailureMessage: ex.StripeError?.Message ?? ex.Message
                )
            );
        }
        finally
        {
            _metrics.RecordProviderLatency(
                stopwatch.Elapsed.TotalMilliseconds,
                PaymentProviderCode.Stripe.ToString(),
                nameof(AuthorizeChargeAsync)
            );
        }
    }

    private static ChargeAuthorizationResult MapToChargeResult(PaymentIntent intent) =>
        intent.Status switch
        {
            "succeeded" => new ChargeAuthorizationResult(intent.Id, PaymentStatus.Succeeded),
            "requires_action" or "requires_source_action" => new ChargeAuthorizationResult(
                intent.Id,
                PaymentStatus.RequiresAction,
                NextActionType: intent.NextAction?.Type,
                NextActionUrl: intent.NextAction?.RedirectToUrl?.Url
            ),
            "processing" => new ChargeAuthorizationResult(intent.Id, PaymentStatus.Processing),
            _ => new ChargeAuthorizationResult(
                intent.Id,
                PaymentStatus.Failed,
                FailureCode: intent.Status,
                FailureMessage: intent.LastPaymentError?.Message
            ),
        };

    public async Task<Result<ChargeAuthorizationResult>> GetChargeStatusAsync(
        string providerChargeReference,
        CancellationToken ct
    )
    {
        var service = new PaymentIntentService(_client);
        try
        {
            var intent = await service.GetAsync(providerChargeReference, cancellationToken: ct);
            return Result.Success(MapToChargeResult(intent));
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe GetChargeStatus failed. Reference={Reference}", providerChargeReference);
            return Result.Failure<ChargeAuthorizationResult>(
                new Error("Stripe.ChargeStatus.Failed", ex.StripeError?.Message ?? ex.Message)
            );
        }
    }

    public async Task<Result<CaptureResult>> CaptureAsync(
        string providerChargeReference,
        Money amount,
        CancellationToken ct
    )
    {
        var service = new PaymentIntentService(_client);
        try
        {
            var intent = await service.CaptureAsync(
                providerChargeReference,
                new PaymentIntentCaptureOptions { AmountToCapture = amount.AmountCents },
                cancellationToken: ct
            );

            var status = intent.Status == "succeeded" ? PaymentStatus.Succeeded : PaymentStatus.Processing;
            return Result.Success(new CaptureResult(intent.Id, status, amount));
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe Capture failed. Reference={Reference}", providerChargeReference);
            return Result.Failure<CaptureResult>(
                new Error("Stripe.Capture.Failed", ex.StripeError?.Message ?? ex.Message)
            );
        }
    }

    public async Task<Result<RefundResult>> RefundAsync(
        string providerChargeReference,
        Money amount,
        string reason,
        CancellationToken ct
    )
    {
        var service = new RefundService(_client);
        var stopwatch = Stopwatch.StartNew();
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
            _logger.LogWarning(ex, "Stripe Refund failed. Reference={Reference}", providerChargeReference);
            return Result.Failure<RefundResult>(
                new Error("Stripe.Refund.Failed", ex.StripeError?.Message ?? ex.Message)
            );
        }
        finally
        {
            _metrics.RecordProviderLatency(
                stopwatch.Elapsed.TotalMilliseconds,
                PaymentProviderCode.Stripe.ToString(),
                nameof(RefundAsync)
            );
        }
    }

    public Task<Result<WebhookVerificationResult>> VerifyWebhookSignatureAsync(
        string rawPayload,
        string signatureHeader,
        string webhookSecret,
        CancellationToken ct
    )
    {
        try
        {
            var stripeEvent = EventUtility.ConstructEvent(rawPayload, signatureHeader, webhookSecret);
            return Task.FromResult(
                Result.Success(new WebhookVerificationResult(stripeEvent.Id, stripeEvent.Type, rawPayload))
            );
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe webhook signature verification failed.");
            return Task.FromResult(
                Result.Failure<WebhookVerificationResult>(new Error("Stripe.WebhookSignature.Invalid", ex.Message))
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
            _logger.LogWarning(ex, "Stripe webhook payload could not be parsed.");
            return Task.FromResult(
                Result.Failure<WebhookEventPayload>(new Error("Stripe.Webhook.ParseFailed", ex.Message))
            );
        }

        var result = eventType switch
        {
            "payment_intent.succeeded" => ParsePaymentIntent(stripeEvent, PaymentStatus.Succeeded),
            "payment_intent.payment_failed" => ParsePaymentIntent(stripeEvent, PaymentStatus.Failed),
            "payment_intent.canceled" => ParsePaymentIntent(stripeEvent, PaymentStatus.Cancelled),
            "charge.refunded" => ParseCharge(stripeEvent),
            "charge.dispute.created" => ParseDispute(stripeEvent),
            _ => Result.Failure<WebhookEventPayload>(
                new Error("Stripe.Webhook.UnsupportedEventType", $"Event type '{eventType}' is not handled.")
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
                RefundedAmountCents: null
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
