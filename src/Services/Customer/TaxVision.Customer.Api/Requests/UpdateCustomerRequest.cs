using TaxVision.Customer.Domain.Customers;

namespace TaxVision.Customer.Api.Requests;

public sealed record UpdateCustomerRequest(
    // Preferencias y contacto
    Language Language,
    // La foto de perfil NO viaja aquí (tiene su endpoint): una edición que la omitía la borraba.
    Guid? OccupationId,
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
    Guid? PrincipalBusinessActivityId = null,
    // ContactPolicy T7.2 — null = preservar el valor actual (PATCH-style).
    bool? EmailEnabled = null,
    bool? SmsEnabled = null
);
