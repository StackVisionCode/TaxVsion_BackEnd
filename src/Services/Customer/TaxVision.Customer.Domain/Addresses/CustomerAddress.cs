using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Customer.Domain.Customers.ValueObjects;

namespace TaxVision.Customer.Domain.Addresses;

public sealed class CustomerAddress : TenantEntity
{
    private CustomerAddress() { }

    public Guid CustomerId { get; private set; }
    public AddressKind Kind { get; private set; }
    public AddressValue Address { get; private set; } = default!;
    public bool IsPrimary { get; private set; }

    internal static Result<CustomerAddress> Create(
        Guid tenantId,
        Guid customerId,
        AddressKind kind,
        AddressValue address,
        bool isPrimary
    )
    {
        if (customerId == Guid.Empty)
            return Result.Failure<CustomerAddress>(new Error("CustomerAddress.Customer", "CustomerId is required."));

        var entity = new CustomerAddress
        {
            Id = Guid.Empty,
            CustomerId = customerId,
            Kind = kind,
            Address = address,
            IsPrimary = isPrimary,
        };
        entity.SetTenant(tenantId);
        return Result.Success(entity);
    }

    internal void MarkPrimary(bool isPrimary) => IsPrimary = isPrimary;

    internal void UpdateAddress(AddressValue newAddress) => Address = newAddress;
}
