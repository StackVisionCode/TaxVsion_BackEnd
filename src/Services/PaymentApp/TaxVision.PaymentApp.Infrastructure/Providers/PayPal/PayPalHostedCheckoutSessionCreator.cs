using System.Globalization;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Infrastructure.Providers.PayPal;

/// <summary>
/// Adapter PayPal del contrato provider-agnostic <see cref="IHostedCheckoutSessionCreator"/>.
/// <see cref="HostedCheckoutCreateRequest.StoreMethodForOffSession"/> se traduce a Vault v3:
/// <c>payment_source.paypal.attributes.vault.store_in_vault=ON_SUCCESS</c> + <c>customer.id</c>
/// derivado del tenant, para que el webhook <c>VAULT.PAYMENT-TOKEN.CREATED</c> pueda atribuir
/// el token al tenant correcto.
/// </summary>
public sealed class PayPalHostedCheckoutSessionCreator(
    PayPalGateway gateway,
    ILogger<PayPalHostedCheckoutSessionCreator> logger
) : IHostedCheckoutSessionCreator
{
    public const string ProviderCodeValue = "PayPal";
    string IHostedCheckoutSessionCreator.ProviderCode => ProviderCodeValue;

    // Convención merchant→PayPal: el customer.id enviado a PayPal es "tenant-{guid-sin-guiones}".
    // Al llegar VAULT.PAYMENT-TOKEN.CREATED, el handler lo revierte para resolver el tenant.
    public static string BuildMerchantCustomerId(Guid tenantId) =>
        $"tenant-{tenantId.ToString("N", CultureInfo.InvariantCulture)}";

    public async Task<Result<HostedCheckoutSession>> CreateAsync(
        HostedCheckoutCreateRequest request,
        CancellationToken ct
    )
    {
        if (request.TenantId == Guid.Empty)
            return Result.Failure<HostedCheckoutSession>(
                new Error("PayPal.HostedCheckout.MissingTenant", "TenantId is required for PayPal hosted checkout.")
            );

        var amountValue = PayPalGateway.FormatAmount(request.AmountCents);
        var merchantCustomerId = request.StoreMethodForOffSession ? BuildMerchantCustomerId(request.TenantId) : null;

        var orderResult = await gateway.CreateOrderAsync(
            new PayPalCreateOrderRequest(
                AmountValue: amountValue,
                CurrencyCode: request.Currency,
                Description: "TAXVISION SAAS",
                ReturnUrl: request.SuccessUrl,
                CancelUrl: request.CancelUrl,
                IdempotencyKey: request.IdempotencyKey,
                Metadata: new Dictionary<string, string>
                {
                    ["tenantId"] = request.TenantId.ToString("N", CultureInfo.InvariantCulture),
                    ["saaSPaymentId"] = request.SaaSPaymentId.ToString("N", CultureInfo.InvariantCulture),
                },
                StoreInVault: request.StoreMethodForOffSession,
                MerchantCustomerId: merchantCustomerId
            ),
            ct
        );

        if (orderResult.IsFailure)
        {
            logger.LogWarning(
                "PayPal hosted checkout creation failed. Tenant={TenantId} StoreForOffSession={StoreForOffSession} Error={ErrorCode}: {ErrorMessage}",
                request.TenantId,
                request.StoreMethodForOffSession,
                orderResult.Error.Code,
                orderResult.Error.Message
            );
            return Result.Failure<HostedCheckoutSession>(orderResult.Error);
        }

        if (request.StoreMethodForOffSession)
            logger.LogInformation(
                "PayPal order {OrderId} created with vault store_in_vault=ON_SUCCESS for tenant {TenantId} (merchant customer {CustomerId}).",
                orderResult.Value.OrderId,
                request.TenantId,
                merchantCustomerId
            );

        return Result.Success(
            new HostedCheckoutSession(
                PaymentId: request.SaaSPaymentId,
                CheckoutUrl: orderResult.Value.ApprovalUrl,
                ExpiresAtUtc: DateTime.UtcNow.AddHours(3)
            )
        );
    }

    public static PaymentProviderCode ProviderCode => PaymentProviderCode.PayPal;
}
