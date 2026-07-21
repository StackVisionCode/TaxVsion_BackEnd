namespace TaxVision.Customer.Application.Customers.Commands.UnassignPreparer;

public sealed record UnassignPreparerCommand(Guid TenantId, Guid CustomerId, Guid UnassignedByUserId);
