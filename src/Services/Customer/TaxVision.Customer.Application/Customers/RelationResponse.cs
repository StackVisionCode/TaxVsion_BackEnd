using TaxVision.Customer.Domain.Relations;

namespace TaxVision.Customer.Application.Customers;

public sealed record RelationResponse(
    Guid Id,
    RelationshipKind RelationshipKind,
    RelationPurpose Purposes,
    string DisplayName,
    string? PrimaryEmail,
    string? PrimaryPhone,
    DateOnly? DateOfBirth,
    bool IsActive
);
