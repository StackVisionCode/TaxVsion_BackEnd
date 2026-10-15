using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Billing;

namespace TaxVision.Subscription.Application.Billing.Commands.StartPaymentMethodSetup;

/// <summary>Valida que el tenant esté en modo Automatic (guardar tarjeta no tiene sentido en Manual) y
/// delega en PaymentApp la creación de la sesión. El webhook de Stripe se encarga del attach + reemplazo
/// del default; acá no se persiste nada propio — es un passthrough controlado.</summary>
public static class StartPaymentMethodSetupHandler
{
    public static async Task<Result<StartPaymentMethodSetupResponse>> Handle(
        StartPaymentMethodSetupCommand command,
        ITenantBillingPreferenceRepository preferences,
        IPaymentMethodSetupClient setupClient,
        ILogger<StartPaymentMethodSetupCommand> logger,
        CancellationToken ct
    )
    {
        if (command.TenantId == Guid.Empty)
            return Result.Failure<StartPaymentMethodSetupResponse>(
                new Error("PaymentMethodSetup.InvalidTenant", "TenantId is required.")
            );
        if (string.IsNullOrWhiteSpace(command.PayerEmail))
            return Result.Failure<StartPaymentMethodSetupResponse>(
                new Error("PaymentMethodSetup.InvalidPayerEmail", "PayerEmail is required.")
            );
        if (string.IsNullOrWhiteSpace(command.SuccessUrl) || string.IsNullOrWhiteSpace(command.CancelUrl))
            return Result.Failure<StartPaymentMethodSetupResponse>(
                new Error("PaymentMethodSetup.InvalidRedirect", "SuccessUrl and CancelUrl are required.")
            );

        var preference = await preferences.GetByTenantIdAsync(command.TenantId, ct);
        if (preference is null || preference.Mode != BillingMode.Automatic)
            return Result.Failure<StartPaymentMethodSetupResponse>(
                new Error(
                    "BillingPreference.NotAutomatic",
                    "A saved payment method only makes sense when billing is set to automatic."
                )
            );

        // Hoy solo Stripe soporta setup-only (Checkout Mode=setup). Para PayPal el usuario administra su
        // método de pago desde su wallet — este botón no aplica. Rechazamos antes del round-trip M2M.
        if (!string.Equals(preference.ProviderCode, "Stripe", StringComparison.OrdinalIgnoreCase))
            return Result.Failure<StartPaymentMethodSetupResponse>(
                new Error(
                    "PaymentMethodSetup.ProviderNotSupported",
                    $"Updating the saved card is not available for {preference.ProviderCode}. "
                        + "Please manage your payment method from your provider's account."
                )
            );

        // Clave de idempotencia por tenant: un segundo click mientras la sesión anterior sigue viva produce
        // la misma Checkout Session (Stripe la reusa por idempotency key). Diferentes intentos a lo largo
        // del tiempo quedan diferenciados por el tick.
        var idempotencyKey = $"pm-setup-{command.TenantId:N}-{DateTime.UtcNow:yyyyMMddHHmmss}";

        var result = await setupClient.CreateSetupSessionAsync(
            new PaymentMethodSetupClientRequest(
                command.TenantId,
                command.PayerEmail,
                command.SuccessUrl,
                command.CancelUrl,
                idempotencyKey,
                preference.ProviderCode
            ),
            ct
        );
        if (result.IsFailure)
            return Result.Failure<StartPaymentMethodSetupResponse>(result.Error);

        logger.LogInformation(
            "Payment method setup session opened for tenant {TenantId} by {UserId}.",
            command.TenantId,
            command.RequestedByUserId
        );
        return Result.Success(new StartPaymentMethodSetupResponse(result.Value.CheckoutUrl, result.Value.ExpiresAtUtc));
    }
}
