using TaxVision.Customer.Domain.ContactPoints;

namespace TaxVision.Customer.Application.Customers;

public sealed record ContactPointResponse(
    Guid Id,
    ContactPointType Type,
    string Value,
    string? Label,
    bool IsPrimary,
    DateTime? VerifiedAtUtc
);
