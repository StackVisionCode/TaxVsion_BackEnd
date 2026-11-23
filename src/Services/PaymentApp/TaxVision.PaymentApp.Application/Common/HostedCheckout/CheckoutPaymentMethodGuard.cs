using BuildingBlocks.Results;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.Common.HostedCheckout;

/// <summary>
/// Los checkouts de una oficina ya activa solo aceptan un método encendido en el catálogo operativo
/// (la misma lista que ve la pantalla). Apagar PayPal lo corta también del lado del servidor.
/// </summary>
public static class CheckoutPaymentMethodGuard
{
    public static async Task<Result> EnsureAvailableAsync(
        IOnboardingPaymentMethodCatalog catalog,
        PaymentProviderCode provider,
        PaymentMethodKind method,
        CancellationToken ct
    )
    {
        var options = await catalog.GetOperationalOptionsAsync(ct);
        if (options.IsFailure)
            return Result.Failure(options.Error);

        var option = options.Value.FirstOrDefault(x => x.Provider == provider && x.Method == method);
        return option is { Enabled: true }
            ? Result.Success()
            : Result.Failure(
                new Error(
                    "PaymentMethod.Disabled",
                    option?.DisabledReason ?? "The selected payment method is not available."
                )
            );
    }
}
