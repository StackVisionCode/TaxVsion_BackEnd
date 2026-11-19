using TaxVision.Mobile.Domain.Vehicles;

namespace TaxVision.Mobile.Application.Vehicles;

public sealed record VehicleResponse(
    Guid Id,
    string Plate,
    string? Make,
    string? Model,
    int? Year,
    string? Color,
    string? Photo,
    decimal CurrentMileage,
    bool IsDefault,
    DateTime CreatedAt,
    DateTime UpdatedAt
)
{
    public static VehicleResponse From(Vehicle vehicle) =>
        new(
            vehicle.Id,
            vehicle.Details.Plate,
            vehicle.Details.Make,
            vehicle.Details.Model,
            vehicle.Details.Year,
            vehicle.Details.Color,
            vehicle.Details.Photo,
            vehicle.Details.CurrentMileage,
            vehicle.IsDefault,
            vehicle.CreatedAtUtc,
            vehicle.UpdatedAtUtc
        );
}
