namespace TaxVision.Subscription.Domain.Seats;

/// <summary>De dónde se originó el seat: comprado directamente con el plan, como parte
/// de un add-on, o creado manualmente por un administrador (cortesía, override).</summary>
public enum SeatSourceType
{
    Plan,
    AddOn,
    Manual,
}
