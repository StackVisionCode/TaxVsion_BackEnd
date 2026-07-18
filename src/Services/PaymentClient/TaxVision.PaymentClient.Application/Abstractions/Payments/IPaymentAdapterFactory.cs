using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.Abstractions.Payments;

/// <summary>
/// Único punto de resolución de un <see cref="IPaymentProvider"/>. Application jamás resuelve
/// providers por nombre string ni hace <c>switch(provider)</c> disperso — todo despacho pasa
/// por <see cref="Resolve"/>.
/// </summary>
public interface IPaymentAdapterFactory
{
    /// <summary>Resuelve el adapter registrado para <paramref name="code"/>. Falla con
    /// <see cref="InvalidOperationException"/> si el provider no está registrado en DI — un
    /// provider ausente es un error de configuración del host, no un caso de negocio
    /// recuperable con <see cref="BuildingBlocks.Results.Result{T}"/>.</summary>
    IPaymentProvider Resolve(PaymentProviderCode code);
}
