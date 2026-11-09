namespace TaxVision.Subscription.Application.Seats.Commands.StartSeatReactivationCheckout;

/// <summary>SUB-14: cuánto se reusa la sesión de una reactivación empezada. Menor que las 24 h que vive la sesión
/// en PaymentApp: pasado el plazo, repetir la clave devolvería una URL caducada.</summary>
public sealed class SeatReactivationOptions
{
    public const string SectionName = "Subscription:SeatReactivation";

    public TimeSpan OpenSessionWindow { get; set; } = TimeSpan.FromHours(23);
}
