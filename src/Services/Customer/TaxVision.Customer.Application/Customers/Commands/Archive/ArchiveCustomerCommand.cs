namespace TaxVision.Customer.Application.Customers.Commands.Archive;

public sealed record ArchiveCustomerCommand(Guid TenantId, Guid CustomerId, Guid ArchivedByUserId);
