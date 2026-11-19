using TaxVision.Mobile.Domain.Vehicles;

namespace TaxVision.Mobile.Tests.Domain;

public sealed class VehicleTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123456789012345678901234567890123")]
    public void Invalid_plate_is_rejected(string? plate) => Assert.True(Details(plate).IsFailure);

    [Theory]
    [InlineData(1885, 0)]
    [InlineData(2101, 0)]
    [InlineData(2020, -1)]
    public void Invalid_year_or_mileage_is_rejected(int year, decimal mileage) =>
        Assert.True(VehicleDetails.Create("ABC", null, null, year, null, null, mileage).IsFailure);

    [Fact]
    public void Normalizes_plate_and_preserves_server_owned_identity()
    {
        var now = new DateTime(2026, 11, 18, 0, 0, 0, DateTimeKind.Utc);
        var tenant = Guid.NewGuid();
        var customer = Guid.NewGuid();
        var vehicle = Vehicle.Create(tenant, customer, Details(" ab-123 ").Value, true, now).Value;
        Assert.Equal("AB-123", vehicle.Details.Plate);
        vehicle.Update(Details("zz-2").Value, false, now.AddMinutes(1));
        Assert.Equal(tenant, vehicle.TenantId);
        Assert.Equal(customer, vehicle.CustomerId);
        Assert.Equal(now, vehicle.CreatedAtUtc);
        Assert.Equal(now.AddMinutes(1), vehicle.UpdatedAtUtc);
        Assert.False(vehicle.IsDefault);
    }

    [Fact]
    public void Empty_owner_is_rejected() =>
        Assert.True(
            Vehicle.Create(Guid.Empty, Guid.NewGuid(), Details("ABC").Value, false, DateTime.UnixEpoch).IsFailure
        );

    [Theory]
    [InlineData("storage:invalid")]
    [InlineData("storage:../../secret")]
    [InlineData("data:image/png;base64,abc")]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://host/photo.png")]
    public void Unsafe_photo_is_rejected(string photo) =>
        Assert.True(VehicleDetails.Create("ABC", null, null, null, null, photo, 0).IsFailure);

    [Theory]
    [InlineData("storage:12345678-1234-1234-1234-123456789abc")]
    [InlineData("https://example.com/car.jpg")]
    public void Persistent_photo_reference_is_accepted(string photo) =>
        Assert.Equal(photo, VehicleDetails.Create("ABC", null, null, null, null, photo, 0).Value.Photo);

    private static BuildingBlocks.Results.Result<VehicleDetails> Details(string? plate) =>
        VehicleDetails.Create(plate, null, null, 2026, null, null, 0);
}
