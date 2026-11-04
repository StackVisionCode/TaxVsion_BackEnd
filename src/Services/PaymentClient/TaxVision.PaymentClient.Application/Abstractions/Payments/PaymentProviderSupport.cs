using BuildingBlocks.Results;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.Abstractions.Payments;

/// <summary>Guardia común de los comandos de configuración: un proveedor sin adaptador no se configura.</summary>
public static class PaymentProviderSupport
{
    public const string NotSupportedCode = "TenantPaymentConfig.ProviderNotSupported";

    public static Result<PaymentProviderDescriptor> RequireSupported(
        this IPaymentAdapterFactory adapters,
        PaymentProviderCode code
    ) =>
        adapters.FindDescriptor(code) is { } descriptor
            ? Result.Success(descriptor)
            : Result.Failure<PaymentProviderDescriptor>(
                new Error(NotSupportedCode, $"Payment provider '{code}' is not supported.")
            );
}
