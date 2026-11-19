using BuildingBlocks.Results;

namespace TaxVision.Mobile.Domain.Vehicles;

public sealed record VehicleDetails
{
    private VehicleDetails() { }

    public string Plate { get; private init; } = string.Empty;
    public string? Make { get; private init; }
    public string? Model { get; private init; }
    public int? Year { get; private init; }
    public string? Color { get; private init; }
    public string? Photo { get; private init; }
    public decimal CurrentMileage { get; private init; }

    public static Result<VehicleDetails> Create(
        string? plate,
        string? make,
        string? model,
        int? year,
        string? color,
        string? photo,
        decimal currentMileage
    )
    {
        if (string.IsNullOrWhiteSpace(plate) || plate.Trim().Length > 32)
            return Result.Failure<VehicleDetails>(VehicleErrors.InvalidPlate);
        if (make?.Trim().Length > 100 || model?.Trim().Length > 100 || color?.Trim().Length > 50)
            return Result.Failure<VehicleDetails>(VehicleErrors.InvalidDetails);
        if (
            year is < 1886 or > 2100
            || currentMileage < 0
            || currentMileage > 999999999.99m
            || decimal.Round(currentMileage, 2) != currentMileage
        )
            return Result.Failure<VehicleDetails>(VehicleErrors.InvalidDetails);
        if (
            !string.IsNullOrWhiteSpace(photo)
            && (
                photo.Length > 2048
                || !Uri.TryCreate(photo, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
            )
        )
            return Result.Failure<VehicleDetails>(VehicleErrors.InvalidPhoto);
        return Result.Success(
            new VehicleDetails
            {
                Plate = plate.Trim().ToUpperInvariant(),
                Make = Clean(make),
                Model = Clean(model),
                Year = year,
                Color = Clean(color),
                Photo = Clean(photo),
                CurrentMileage = currentMileage,
            }
        );
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
