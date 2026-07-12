namespace TaxVision.Customer.Application.Customers.Commands.RemoveContactPoint;

public sealed record RemoveContactPointCommand(
    Guid TenantId,
    Guid CustomerId,
    Guid ContactPointId,
    Guid ModifiedByUserId
);
