namespace TaxVision.Customer.Application.Customers;

public sealed record CustomerExistsResponse(bool EmailExists, bool TaxIdentifierExists, Guid? ExistingCustomerId);
