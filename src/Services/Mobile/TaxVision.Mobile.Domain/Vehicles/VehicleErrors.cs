using BuildingBlocks.Results;

namespace TaxVision.Mobile.Domain.Vehicles;

public static class VehicleErrors
{
    public static readonly Error NotFound = new("Vehicle.NotFound", "Vehicle was not found.");
    public static readonly Error InvalidOwner = new("Vehicle.InvalidOwner", "A tenant and customer are required.");
    public static readonly Error InvalidPlate = new(
        "Vehicle.InvalidPlate",
        "Plate is required and must not exceed 32 characters."
    );
    public static readonly Error InvalidDetails = new(
        "Vehicle.InvalidDetails",
        "Vehicle details exceed the allowed length, year or mileage range."
    );
    public static readonly Error InvalidPhoto = new(
        "Vehicle.InvalidPhoto",
        "Photo must be an HTTPS URL of at most 2048 characters."
    );
    public static readonly Error InvalidPage = new(
        "Vehicle.InvalidPage",
        "Page must be positive and size must be between 1 and 100."
    );
}
