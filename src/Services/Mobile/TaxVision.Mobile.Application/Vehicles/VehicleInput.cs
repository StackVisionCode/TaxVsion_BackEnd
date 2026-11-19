using BuildingBlocks.Results;
using TaxVision.Mobile.Domain.Vehicles;

namespace TaxVision.Mobile.Application.Vehicles;

public sealed record VehicleInput(
    string? Plate,
    string? Make = null,
    string? Model = null,
    int? Year = null,
    string? Color = null,
    string? Photo = null,
    decimal CurrentMileage = 0,
    bool IsDefault = false
)
{
    public Result<VehicleDetails> Validate() =>
        VehicleDetails.Create(Plate, Make, Model, Year, Color, Photo, CurrentMileage);
}
