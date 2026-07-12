namespace TaxVision.Customer.Application.Customers.Commands.Archive;

public sealed record ArchiveCustomerCommand(Guid CustomerId, Guid ArchivedByUserId);
