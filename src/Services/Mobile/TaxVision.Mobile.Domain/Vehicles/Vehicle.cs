using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Mobile.Domain.Vehicles;

public sealed class Vehicle : TenantEntity
{
    private Vehicle() { }

    public Guid CustomerId { get; private set; }
    public VehicleDetails Details { get; private set; } = null!;
    public bool IsDefault { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static Result<Vehicle> Create(
        Guid tenantId,
        Guid customerId,
        VehicleDetails details,
        bool isDefault,
        DateTime nowUtc
    )
    {
        if (tenantId == Guid.Empty || customerId == Guid.Empty)
            return Result.Failure<Vehicle>(VehicleErrors.InvalidOwner);
        var vehicle = new Vehicle
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            Details = details,
            IsDefault = isDefault,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
        vehicle.SetTenant(tenantId);
        return Result.Success(vehicle);
    }

    public void Update(VehicleDetails details, bool isDefault, DateTime nowUtc)
    {
        Details = details;
        IsDefault = isDefault;
        UpdatedAtUtc = nowUtc;
    }

    public void ClearDefault(DateTime nowUtc)
    {
        IsDefault = false;
        UpdatedAtUtc = nowUtc;
    }
}
