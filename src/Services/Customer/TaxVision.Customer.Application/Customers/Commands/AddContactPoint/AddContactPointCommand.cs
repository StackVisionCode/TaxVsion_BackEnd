using TaxVision.Customer.Domain.ContactPoints;

namespace TaxVision.Customer.Application.Customers.Commands.AddContactPoint;

public sealed record AddContactPointCommand(
    Guid TenantId,
    Guid CustomerId,
    Guid ModifiedByUserId,
    ContactPointType Type,
    string Value,
    string? Label,
    bool IsPrimary
);
