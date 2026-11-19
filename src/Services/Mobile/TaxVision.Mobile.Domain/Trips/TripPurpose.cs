namespace TaxVision.Mobile.Domain.Trips;

public static class TripPurpose
{
    public static bool IsValid(string? purpose) =>
        purpose is "business" or "medical" or "moving" or "personal" or "charity";
}
