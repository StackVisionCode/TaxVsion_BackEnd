using TaxVision.Customer.Domain.Customers;

namespace TaxVision.Customer.Api.Requests;

public sealed record UpdateCustomerRequest(
    // Preferencias y contacto
    Language Language,
    PreferredChannel PreferredChannel,
    Guid? OccupationId,
    Guid? ProfilePictureFileId,
    string PrimaryEmail,
    string? PrimaryPhone,
    // Identidad Individual (opcionales, solo aplican a Individual)
    string? FirstName = null,
    string? MiddleName = null,
    string? LastName = null,
    string? Prefix = null,
    string? Suffix = null,
    DateOnly? DateOfBirth = null,
    // Identidad Business (opcionales, solo aplican a Business)
    string? LegalName = null,
    string? Dba = null,
    BusinessStructure? BusinessStructure = null,
    DateOnly? FormationDate = null,
    Guid? PrincipalBusinessActivityId = null
);
