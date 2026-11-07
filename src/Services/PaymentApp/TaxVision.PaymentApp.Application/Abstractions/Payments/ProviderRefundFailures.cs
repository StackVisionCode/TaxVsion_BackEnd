using BuildingBlocks.Results;

namespace TaxVision.PaymentApp.Application.Abstractions.Payments;

/// <summary>Clasifica el error de <see cref="IPaymentProvider.RefundAsync"/>.</summary>
public static class ProviderRefundFailures
{
    /// <summary>Pasajero: se reintenta con la misma clave. Los adaptadores lo marcan con el sufijo <c>Unavailable</c>.</summary>
    public static bool IsTransient(Error error) => error.Code.EndsWith("Unavailable", StringComparison.Ordinal);
}
