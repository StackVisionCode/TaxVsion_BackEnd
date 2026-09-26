namespace TaxVision.Customer.Application.Customers.Commands.BulkAssign;

public sealed record BulkAssignCustomersCommand(
    Guid TenantId,
    Guid UserId,
    IReadOnlyList<Guid> CustomerIds,
    Guid AssignedByUserId
);
