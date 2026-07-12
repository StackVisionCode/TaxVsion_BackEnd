using TaxVision.Customer.Domain.ContactPoints;

namespace TaxVision.Customer.Application.Customers.Commands.UpdateContactPoint;

public sealed record UpdateContactPointCommand(
    Guid TenantId,
    Guid CustomerId,
    Guid ContactPointId,
    Guid ModifiedByUserId,
    ContactPointType Type,
    string Value,
    string? Label,
    bool IsPrimary
);
