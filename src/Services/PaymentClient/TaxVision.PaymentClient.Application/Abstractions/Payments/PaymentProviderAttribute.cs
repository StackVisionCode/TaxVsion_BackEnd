using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.Abstractions.Payments;

/// <summary>
/// Decora una implementación de <see cref="IPaymentProvider"/> para que el auto-registro
/// reflectivo en <c>AddPaymentProviders(...)</c> la descubra y la registre en DI keyed por
/// <see cref="Code"/>. Agregar un provider nuevo requiere únicamente crear la clase y
/// decorarla — cero cambios en <c>DependencyInjection.cs</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class PaymentProviderAttribute(PaymentProviderCode code) : Attribute
{
    public PaymentProviderCode Code { get; } = code;
}
