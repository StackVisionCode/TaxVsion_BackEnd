namespace TaxVision.Customer.Application.Customers.Commands.RevealTaxIdentifier;

public sealed record RevealTaxIdentifierCommand(
    Guid TenantId,
    Guid CustomerId,
    Guid RequestedByUserId,
    string CorrelationId,
    string? IpAddress,
    string? UserAgent
);
