using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.ProviderCustomers;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.ProviderCustomers.Commands.ProcessStripePaymentMethodWebhook;

/// <summary>
/// Verifica la firma del webhook, traduce el evento al dato canónico y adjunta el método a
/// <see cref="TenantProviderCustomer"/>. Si la metadata no permite atribuir el tenant, acepta
/// el webhook y acknowledgea — mejor perder un save que mutar un tenant equivocado.
/// </summary>
public static class ProcessStripePaymentMethodWebhookHandler
{
    public static async Task<Result> Handle(
        ProcessStripePaymentMethodWebhookCommand command,
        IPaymentAdapterFactory providerFactory,
        IProviderWebhookSecrets webhookSecrets,
        IStripePaymentMethodWebhookTranslator translator,
        ITenantProviderCustomerRepository customers,
        IUnitOfWork unitOfWork,
        ILogger<TenantProviderCustomer> logger,
        CancellationToken ct
    )
    {
        var adapter = providerFactory.Resolve(PaymentProviderCode.Stripe);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Stripe-Signature"] = command.SignatureHeader,
        };

        var verification = await adapter.VerifyWebhookSignatureAsync(
            new ProviderWebhookVerificationRequest(
                command.RawPayload,
                headers,
                webhookSecrets.GetWebhookSecret(PaymentProviderCode.Stripe),
                webhookSecrets.GetWebhookId(PaymentProviderCode.Stripe)
            ),
            ct
        );
        if (verification.IsFailure)
        {
            logger.LogWarning(
                "Rejected Stripe payment-method webhook with invalid signature: {Error}",
                verification.Error.Message
            );
            return Result.Failure(verification.Error);
        }

        var translationResult = await translator.TranslateAsync(command.RawPayload, verification.Value.EventType, ct);
        if (translationResult.IsFailure)
            return Result.Failure(translationResult.Error);
        if (translationResult.Value is not { } data)
            return Result.Success(); // evento sin método adjuntable: ack silencioso

        if (data.TenantId is not { } tenantId)
        {
            logger.LogWarning(
                "Stripe payment-method webhook {EventId} ({EventType}) for customer {Customer} could not be attributed to a tenant; acknowledging without persisting.",
                verification.Value.ProviderEventId,
                verification.Value.EventType,
                data.StripeCustomerId
            );
            return Result.Success();
        }

        var customer = await customers.GetByTenantAndProviderAsync(tenantId, PaymentProviderCode.Stripe, ct);
        var isNew = customer is null;
        if (customer is null)
        {
            var referenceResult = ProviderCustomerReference.Create(PaymentProviderCode.Stripe, data.StripeCustomerId);
            if (referenceResult.IsFailure)
                return Result.Failure(referenceResult.Error);

            var registered = TenantProviderCustomer.Register(
                tenantId,
                PaymentProviderCode.Stripe,
                referenceResult.Value,
                string.IsNullOrWhiteSpace(data.PayerEmail) ? "stripe@taxvision.local" : data.PayerEmail,
                DateTime.UtcNow
            );
            if (registered.IsFailure)
                return Result.Failure(registered.Error);

            customer = registered.Value;
        }

        // "Update card" (setup-only) reemplaza el default — detacha la tarjeta vieja. El flujo
        // original (payment con setup_future_usage=off_session) adjunta sin detachar.
        var nowUtc = DateTime.UtcNow;
        if (data.IsSetupMode)
        {
            var replaceResult = customer.ReplaceDefaultPaymentMethod(
                methodReference: data.MethodReference,
                brand: data.Brand,
                last4: data.Last4,
                expMonth: data.ExpMonth,
                expYear: data.ExpYear,
                nowUtc: nowUtc
            );
            if (replaceResult.IsFailure)
                return Result.Failure(replaceResult.Error);
        }
        else
        {
            var attachResult = customer.AttachPaymentMethod(
                methodReference: data.MethodReference,
                brand: data.Brand,
                last4: data.Last4,
                expMonth: data.ExpMonth,
                expYear: data.ExpYear,
                setAsDefault: true,
                nowUtc: nowUtc
            );
            if (attachResult.IsFailure)
            {
                if (
                    string.Equals(
                        attachResult.Error.Code,
                        "TenantProviderCustomer.MethodAlreadyAttached",
                        StringComparison.Ordinal
                    )
                )
                {
                    logger.LogInformation(
                        "Stripe payment method {Method} already attached for tenant {TenantId}; ignoring redelivery.",
                        data.MethodReference,
                        tenantId
                    );
                    return Result.Success();
                }
                return Result.Failure(attachResult.Error);
            }
        }

        if (isNew)
            await customers.AddAsync(customer, ct);

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Stripe payment method {Method} ({Brand} ****{Last4}) attached for tenant {TenantId}.",
            data.MethodReference,
            data.Brand,
            data.Last4,
            tenantId
        );
        return Result.Success();
    }
}
