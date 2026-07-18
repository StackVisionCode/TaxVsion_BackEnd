using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Infrastructure.Providers.Intellipay;

/// <summary>
/// Adapter production-ready para Intellipay, portado del CRM legado con las 12 correcciones
/// del §44.7.2 (TenantId en vez de CompanyId, provider como enum, Result&lt;T&gt; en vez de
/// excepciones, sin LINQ en aggregates, sin <c>virtual</c>, timezone via TimeProvider, config
/// vía <see cref="ISecretProtector"/>-free options porque las credenciales son globales de
/// plataforma, no por-tenant). <c>charge_card</c> en Intellipay autoriza y captura en un solo
/// paso — no existe un flujo de two-phase capture separado como en Stripe, por eso
/// <see cref="CaptureAsync"/> es un no-op que confirma el estado ya alcanzado en
/// <see cref="AuthorizeChargeAsync"/>.
/// </summary>
[PaymentProvider(PaymentProviderCode.Intellipay)]
public sealed class IntellipayAdapter(IntellipayGateway gateway, ILogger<IntellipayAdapter> logger) : IPaymentProvider
{
    public PaymentProviderCode Code => PaymentProviderCode.Intellipay;
    public ProviderCapabilities Capabilities => IntellipayCapabilities.Instance;

    public async Task<Result<ProviderCustomerToken>> GetOrCreateCustomerAsync(
        Guid tenantId,
        string email,
        string? name,
        CancellationToken ct
    )
    {
        var response = await gateway.CreateCustomerAsync(
            new IntellipayCreateCustomerRequest(
                Account: tenantId.ToString("N"),
                Email: email,
                FirstName: name ?? "TaxVision Customer"
            ),
            ct
        );

        if (!response.IsApproved || string.IsNullOrWhiteSpace(response.CustId))
        {
            logger.LogWarning(
                "Intellipay create_customer failed for tenant {TenantId}: {Message}",
                tenantId,
                response.Message
            );
            return Result.Failure<ProviderCustomerToken>(
                new Error("Intellipay.CreateCustomer.Failed", response.Message ?? "Unknown Intellipay error.")
            );
        }

        return Result.Success(new ProviderCustomerToken(response.CustId, PaymentProviderCode.Intellipay));
    }

    public async Task<Result<ChargeAuthorizationResult>> AuthorizeChargeAsync(
        ChargeAuthorizationRequest request,
        CancellationToken ct
    )
    {
        var response = await gateway.ChargeCardAsync(
            new IntellipayChargeRequest(
                CustomerId: request.Customer.Token,
                AmountCents: request.Amount.AmountCents,
                Description: request.Descriptor.Value,
                IdempotencyKey: request.IdempotencyKey.Value
            ),
            ct
        );

        if (response.IsApproved)
        {
            return Result.Success(
                new ChargeAuthorizationResult(
                    ProviderChargeReference: response.TransactionId ?? string.Empty,
                    Status: PaymentStatus.Succeeded
                )
            );
        }

        return Result.Success(
            new ChargeAuthorizationResult(
                ProviderChargeReference: response.TransactionId ?? string.Empty,
                Status: PaymentStatus.Failed,
                FailureCode: response.Response,
                FailureMessage: response.Message
            )
        );
    }

    public Task<Result<CaptureResult>> CaptureAsync(
        string providerChargeReference,
        Money amount,
        CancellationToken ct
    ) => Task.FromResult(Result.Success(new CaptureResult(providerChargeReference, PaymentStatus.Succeeded, amount)));

    public async Task<Result<RefundResult>> RefundAsync(
        string providerChargeReference,
        Money amount,
        string reason,
        CancellationToken ct
    )
    {
        var response = await gateway.RefundAsync(
            new IntellipayRefundRequest(
                TransactionId: providerChargeReference,
                AmountCents: amount.AmountCents,
                Reason: reason
            ),
            ct
        );

        if (!response.IsApproved)
        {
            logger.LogWarning(
                "Intellipay refund failed for {Reference}: {Message}",
                providerChargeReference,
                response.Message
            );
            return Result.Failure<RefundResult>(
                new Error("Intellipay.Refund.Failed", response.Message ?? "Unknown Intellipay error.")
            );
        }

        return Result.Success(
            new RefundResult(response.TransactionId ?? providerChargeReference, PaymentStatus.Refunded, amount)
        );
    }

    /// <summary>Intellipay no firma sus callbacks IPN. La mitigación (IP allowlist + timestamp
    /// + status oráculo vía <see cref="IntellipayGateway.GetTransactionStatusAsync"/>) vive en
    /// <c>IntellipayIpnValidator</c> (Fase B) — este método existe solo para cumplir el
    /// contrato <see cref="IPaymentProvider"/> y falla explícitamente para que ningún caller
    /// lo use por error como si fuera HMAC real.</summary>
    public Task<Result<WebhookVerificationResult>> VerifyWebhookSignatureAsync(
        string rawPayload,
        string signatureHeader,
        string webhookSecret,
        CancellationToken ct
    ) =>
        Task.FromResult(
            Result.Failure<WebhookVerificationResult>(
                new Error(
                    "Intellipay.WebhookSignature.NotSupported",
                    "Intellipay does not sign IPN callbacks. Use IntellipayIpnValidator instead."
                )
            )
        );

    public Task<Result<WebhookEventPayload>> ParseWebhookEventAsync(
        string rawPayload,
        string eventType,
        CancellationToken ct
    ) =>
        Task.FromResult(
            Result.Failure<WebhookEventPayload>(
                new Error(
                    "Intellipay.Webhook.NotSupported",
                    "Intellipay IPN parsing is handled by IntellipayIpnValidator, not this generic path."
                )
            )
        );

    public async Task<Result<ChargeAuthorizationResult>> GetChargeStatusAsync(
        string providerChargeReference,
        CancellationToken ct
    )
    {
        var response = await gateway.GetTransactionStatusAsync(providerChargeReference, ct);

        return Result.Success(
            new ChargeAuthorizationResult(
                ProviderChargeReference: providerChargeReference,
                Status: response.IsApproved ? PaymentStatus.Succeeded : PaymentStatus.Failed,
                FailureCode: response.IsApproved ? null : response.Response,
                FailureMessage: response.IsApproved ? null : response.Message
            )
        );
    }

    /// <summary>Intellipay no modela "adjuntar una tarjeta extra" — el custId ya es el token
    /// reusable. Gestión de múltiples tarjetas por customer queda para cuando el negocio lo
    /// pida (§44.12: Intellipay production-ready cubre charge/refund/status, no card vault).</summary>
    public Task<Result<SavedPaymentMethodInfo>> AttachPaymentMethodAsync(
        ProviderCustomerToken customer,
        string paymentMethodReference,
        CancellationToken ct
    ) =>
        Task.FromResult(
            Result.Failure<SavedPaymentMethodInfo>(
                new Error(
                    "Intellipay.PaymentMethod.NotSupported",
                    "Intellipay does not support attaching additional payment methods yet."
                )
            )
        );

    public Task<Result> DetachPaymentMethodAsync(string paymentMethodReference, CancellationToken ct) =>
        Task.FromResult(
            Result.Failure(
                new Error(
                    "Intellipay.PaymentMethod.NotSupported",
                    "Intellipay does not support detaching payment methods yet."
                )
            )
        );
}
