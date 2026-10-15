using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;

namespace TaxVision.PaymentApp.Application.PaymentMethodSetup.Commands;

/// <summary>Pide al creator por defecto (Stripe, el único que hoy soporta setup-only) una sesión que
/// solo captura método — sin cobro ni <c>SaaSPayment</c>. Otros providers quedan fuera por diseño:
/// el landing dirige "Update card" únicamente a Stripe.</summary>
public static class CreatePaymentMethodSetupSessionHandler
{
    public static async Task<Result<PaymentMethodSetupSessionResponse>> Handle(
        CreatePaymentMethodSetupSessionCommand command,
        IHostedCheckoutSessionCreatorFactory creators,
        ILogger<CreatePaymentMethodSetupSessionCommand> logger,
        CancellationToken ct
    )
    {
        if (command.TenantId == Guid.Empty)
            return Result.Failure<PaymentMethodSetupSessionResponse>(
                new Error("PaymentMethodSetup.InvalidTenant", "TenantId is required.")
            );
        if (string.IsNullOrWhiteSpace(command.PayerEmail))
            return Result.Failure<PaymentMethodSetupSessionResponse>(
                new Error("PaymentMethodSetup.InvalidPayerEmail", "PayerEmail is required.")
            );

        // Setup-only es Stripe-only hoy. PayPal usa su propio flujo de wallet.
        var providerCode = string.IsNullOrWhiteSpace(command.Provider) ? "Stripe" : command.Provider;
        if (!string.Equals(providerCode, "Stripe", StringComparison.OrdinalIgnoreCase))
            return Result.Failure<PaymentMethodSetupSessionResponse>(
                new Error(
                    "PaymentMethodSetup.ProviderNotSupported",
                    $"Provider '{command.Provider}' does not support setup-only sessions."
                )
            );

        var creator = creators.For(providerCode);
        if (creator is null)
            return Result.Failure<PaymentMethodSetupSessionResponse>(
                new Error("PaymentMethodSetup.CreatorNotRegistered", $"No creator registered for '{providerCode}'.")
            );

        var request = new HostedCheckoutCreateRequest(
            TenantId: command.TenantId,
            SaaSPaymentId: Guid.Empty,
            AmountCents: 0,
            Currency: "USD",
            PayerEmail: command.PayerEmail,
            SuccessUrl: command.SuccessUrl,
            CancelUrl: command.CancelUrl,
            IdempotencyKey: command.IdempotencyKey,
            Provider: command.Provider,
            Method: null,
            StoreMethodForOffSession: true,
            IntentType: HostedCheckoutIntentType.Setup
        );

        var result = await creator.CreateAsync(request, ct);
        if (result.IsFailure)
        {
            logger.LogWarning(
                "Payment method setup session failed for tenant {TenantId}: {Error}",
                command.TenantId,
                result.Error.Code
            );
            return Result.Failure<PaymentMethodSetupSessionResponse>(result.Error);
        }

        logger.LogInformation("Payment method setup session opened for tenant {TenantId}.", command.TenantId);
        return Result.Success(
            new PaymentMethodSetupSessionResponse(result.Value.CheckoutUrl, result.Value.ExpiresAtUtc)
        );
    }
}
