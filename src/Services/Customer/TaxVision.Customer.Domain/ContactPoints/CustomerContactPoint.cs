using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Customer.Domain.ContactPoints;

public sealed class CustomerContactPoint : TenantEntity
{
    private CustomerContactPoint() { }

    public Guid CustomerId { get; private set; }
    public ContactPointType Type { get; private set; }
    public string Value { get; private set; } = default!;
    public string NormalizedValue { get; private set; } = default!;
    public string? Label { get; private set; }
    public bool IsPrimary { get; private set; }
    public DateTime? VerifiedAtUtc { get; private set; }

    internal static Result<CustomerContactPoint> Create(
        Guid tenantId,
        Guid customerId,
        ContactPointType type,
        string value,
        string normalizedValue,
        string? label,
        bool isPrimary
    )
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(normalizedValue))
            return Result.Failure<CustomerContactPoint>(new Error("ContactPoint.Value", "Value is required."));

        var entity = new CustomerContactPoint
        {
            Id = Guid.Empty,
            CustomerId = customerId,
            Type = type,
            Value = value,
            NormalizedValue = normalizedValue,
            Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim(),
            IsPrimary = isPrimary,
        };
        entity.SetTenant(tenantId);
        return Result.Success(entity);
    }

    internal void MarkPrimary(bool isPrimary) => IsPrimary = isPrimary;

    internal void MarkVerified(DateTime atUtc) => VerifiedAtUtc = atUtc;
}
