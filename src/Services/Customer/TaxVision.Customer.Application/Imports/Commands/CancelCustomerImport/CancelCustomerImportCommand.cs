namespace TaxVision.Customer.Application.Imports.Commands.CancelCustomerImport;

public sealed record CancelCustomerImportCommand(Guid TenantId, Guid ImportAttemptId, Guid RequestedByUserId);
