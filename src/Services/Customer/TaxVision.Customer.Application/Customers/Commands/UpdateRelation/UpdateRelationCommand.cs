using TaxVision.Customer.Domain.Relations;

namespace TaxVision.Customer.Application.Customers.Commands.UpdateRelation;

public sealed record UpdateRelationCommand(
    Guid TenantId,
    Guid CustomerId,
    Guid RelationId,
    Guid ModifiedByUserId,
    RelationshipKind RelationshipKind,
    RelationPurpose Purposes,
    string FirstName,
    string LastName,
    string? MiddleName,
    string? Prefix,
    string? Suffix,
    string? PrimaryEmail,
    string? PrimaryPhone,
    DateOnly? DateOfBirth,
    string? AddressLine1,
    string? AddressLine2,
    string? AddressCity,
    string? AddressRegion,
    string? AddressPostalCode,
    string? AddressCountryCode
);
