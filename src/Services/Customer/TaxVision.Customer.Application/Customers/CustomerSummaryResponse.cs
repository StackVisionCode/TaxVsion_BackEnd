using TaxVision.Customer.Domain.Customers;

namespace TaxVision.Customer.Application.Customers;

public sealed record CustomerSummaryResponse(
    Guid Id,
    CustomerKind Kind,
    CustomerStatus Status,
    string DisplayName,
    string PrimaryEmail,
    string? PrimaryPhone,
    DateTime CreatedAtUtc
);
