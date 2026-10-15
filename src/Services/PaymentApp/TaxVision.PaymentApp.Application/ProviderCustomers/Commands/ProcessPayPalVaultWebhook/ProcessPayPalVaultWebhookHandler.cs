using System.Globalization;
using System.Text.Json;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.ProviderCustomers;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.ProviderCustomers.Commands.ProcessPayPalVaultWebhook;

/// <summary>
/// Procesa un webhook de PayPal Vault ya recibido crudo. Verifica la firma con el adapter de
/// PayPal, parsea el payment token y lo adjunta al <c>TenantProviderCustomer</c> cuyo
/// <c>customer.id</c> fue sembrado por <c>PayPalHostedCheckoutSessionCreator</c> en formato
/// <c>tenant-{guidN}</c>. Si el <c>customer.id</c> no se puede atribuir, se registra y se ack —
/// mejor perder un save que mutar un tenant equivocado.
/// </summary>
public static class ProcessPayPalVaultWebhookHandler
{
    private const string MerchantCustomerIdPrefix = "tenant-";

    public static async Task<Result> Handle(
        ProcessPayPalVaultWebhookCommand command,
        IPaymentAdapterFactory providerFactory,
        IProviderWebhookSecrets webhookSecrets,
        ITenantProviderCustomerRepository customers,
        IUnitOfWork unitOfWork,
        ILogger<TenantProviderCustomer> logger,
        CancellationToken ct
    )
    {
        var adapter = providerFactory.Resolve(PaymentProviderCode.PayPal);

        var verification = await adapter.VerifyWebhookSignatureAsync(
            new ProviderWebhookVerificationRequest(
                command.RawPayload,
                command.Headers,
                webhookSecrets.GetWebhookSecret(PaymentProviderCode.PayPal),
                webhookSecrets.GetWebhookId(PaymentProviderCode.PayPal)
            ),
            ct
        );
        if (verification.IsFailure)
        {
            logger.LogWarning(
                "Rejected PayPal vault webhook with invalid signature: {Error}",
                verification.Error.Message
            );
            return Result.Failure(verification.Error);
        }

        var parsed = ParseVaultEvent(command.RawPayload, logger);
        if (parsed is null)
            return Result.Success(); // evento ignorable: log + ack

        var tenantId = ExtractTenantId(parsed.MerchantCustomerId);
        if (tenantId is null)
        {
            logger.LogWarning(
                "PayPal vault webhook {EventId} ({EventType}) could not be attributed: merchant customer id '{CustomerId}' is not in expected format.",
                verification.Value.ProviderEventId,
                verification.Value.EventType,
                parsed.MerchantCustomerId
            );
            return Result.Success();
        }

        if (string.Equals(verification.Value.EventType, "VAULT.PAYMENT-TOKEN.DELETED", StringComparison.Ordinal))
        {
            // Delete remoto: por ahora solo logueamos; el detach manual del front sigue manejándolo.
            logger.LogInformation(
                "PayPal vault token {TokenId} deleted upstream for tenant {TenantId}; local record untouched.",
                parsed.PaymentTokenId,
                tenantId
            );
            return Result.Success();
        }

        var customer = await customers.GetByTenantAndProviderAsync(tenantId.Value, PaymentProviderCode.PayPal, ct);
        var isNew = customer is null;
        if (customer is null)
        {
            var referenceResult = ProviderCustomerReference.Create(
                PaymentProviderCode.PayPal,
                parsed.MerchantCustomerId ?? string.Empty
            );
            if (referenceResult.IsFailure)
                return Result.Failure(referenceResult.Error);

            var createResult = TenantProviderCustomer.Register(
                tenantId.Value,
                PaymentProviderCode.PayPal,
                referenceResult.Value,
                parsed.PayerEmail ?? "paypal@taxvision.local",
                DateTime.UtcNow
            );
            if (createResult.IsFailure)
                return Result.Failure(createResult.Error);

            customer = createResult.Value;
        }

        var attachResult = customer.AttachPaymentMethod(
            methodReference: parsed.PaymentTokenId,
            brand: parsed.Brand,
            last4: parsed.Last4,
            expMonth: parsed.ExpMonth,
            expYear: parsed.ExpYear,
            setAsDefault: true,
            nowUtc: DateTime.UtcNow
        );
        if (attachResult.IsFailure)
        {
            // Idempotencia: ya adjuntado por un redelivery previo — ack.
            if (
                string.Equals(
                    attachResult.Error.Code,
                    "TenantProviderCustomer.MethodAlreadyAttached",
                    StringComparison.Ordinal
                )
            )
            {
                logger.LogInformation(
                    "PayPal vault token {TokenId} already attached for tenant {TenantId}; ignoring redelivery.",
                    parsed.PaymentTokenId,
                    tenantId
                );
                return Result.Success();
            }
            return Result.Failure(attachResult.Error);
        }

        if (isNew)
            await customers.AddAsync(customer, ct);

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "PayPal vault token {TokenId} ({Brand} ****{Last4}) attached for tenant {TenantId}.",
            parsed.PaymentTokenId,
            parsed.Brand,
            parsed.Last4,
            tenantId
        );
        return Result.Success();
    }

    private static Guid? ExtractTenantId(string? merchantCustomerId)
    {
        if (string.IsNullOrWhiteSpace(merchantCustomerId))
            return null;
        if (!merchantCustomerId.StartsWith(MerchantCustomerIdPrefix, StringComparison.Ordinal))
            return null;
        var hex = merchantCustomerId.Substring(MerchantCustomerIdPrefix.Length);
        return Guid.TryParseExact(hex, "N", out var parsed) ? parsed : null;
    }

    private static VaultTokenEventData? ParseVaultEvent(string rawPayload, ILogger logger)
    {
        try
        {
            using var document = JsonDocument.Parse(rawPayload);
            var root = document.RootElement;
            if (!root.TryGetProperty("resource", out var resource))
                return null;

            var paymentTokenId = TryGetString(resource, "id");
            if (string.IsNullOrWhiteSpace(paymentTokenId))
                return null;

            var merchantCustomerId = TryGetString(resource, "customer", "id");

            // PayPal account token (wallet): payment_source.paypal.email_address, sin exp.
            var email = TryGetString(resource, "payment_source", "paypal", "email_address");
            if (!string.IsNullOrWhiteSpace(email))
            {
                // last4 convencional: iniciales del email (hasta 4 chars) para que el UI muestre algo
                // útil sin almacenar el email entero en un campo que semánticamente es last4.
                var prefix = email.Split('@')[0];
                var last4 = prefix.Length <= 4 ? prefix : prefix[..4];
                return new VaultTokenEventData(
                    paymentTokenId,
                    merchantCustomerId,
                    Brand: "paypal",
                    Last4: last4,
                    ExpMonth: 0,
                    ExpYear: 0,
                    PayerEmail: email
                );
            }

            // Card token.
            var cardLast4 = TryGetString(resource, "payment_source", "card", "last_digits");
            if (!string.IsNullOrWhiteSpace(cardLast4))
            {
                var brand = TryGetString(resource, "payment_source", "card", "brand") ?? "card";
                var expiry = TryGetString(resource, "payment_source", "card", "expiry"); // "YYYY-MM"
                var (expMonth, expYear) = ParseCardExpiry(expiry);
                return new VaultTokenEventData(
                    paymentTokenId,
                    merchantCustomerId,
                    Brand: brand.ToLowerInvariant(),
                    Last4: cardLast4,
                    ExpMonth: expMonth,
                    ExpYear: expYear,
                    PayerEmail: null
                );
            }

            return null;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "PayPal vault webhook payload could not be parsed as JSON.");
            return null;
        }
    }

    private static (int Month, int Year) ParseCardExpiry(string? expiry)
    {
        if (string.IsNullOrWhiteSpace(expiry))
            return (0, 0);
        var parts = expiry.Split('-');
        if (parts.Length != 2)
            return (0, 0);
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var year))
            return (0, 0);
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var month))
            return (0, 0);
        return (month, year);
    }

    private static string? TryGetString(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (!current.TryGetProperty(segment, out current))
                return null;
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    private sealed record VaultTokenEventData(
        string PaymentTokenId,
        string? MerchantCustomerId,
        string Brand,
        string Last4,
        int ExpMonth,
        int ExpYear,
        string? PayerEmail
    );
}
