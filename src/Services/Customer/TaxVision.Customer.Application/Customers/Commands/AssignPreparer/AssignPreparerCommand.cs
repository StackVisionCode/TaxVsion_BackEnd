namespace TaxVision.Customer.Application.Customers.Commands.AssignPreparer;

public sealed record AssignPreparerCommand(Guid TenantId, Guid CustomerId, Guid PreparerUserId, Guid AssignedByUserId);
