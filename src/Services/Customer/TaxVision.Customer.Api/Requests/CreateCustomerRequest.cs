using TaxVision.Customer.Domain.Customers;

namespace TaxVision.Customer.Api.Requests;

public sealed record CreateCustomerRequest(
    CustomerKind Kind,
    string? FirstName,
    string? MiddleName,
    string? LastName,
    string? Prefix,
    string? Suffix,
    string? LegalName,
    BusinessStructure? BusinessStructure,
    string? Dba,
    DateOnly? FormationDate,
    Guid? PrincipalBusinessActivityId,
    DateOnly? DateOfBirth,
    Guid? OccupationId,
    string PrimaryEmail,
    string? PrimaryPhone,
    Language Language,
    /// <summary>
    /// Si ya existe un cliente igual: en <c>false</c> se responde 409 con el id del que está; en
    /// <c>true</c> se le aplican encima los datos que vienen, en vez de crear otro.
    /// </summary>
    bool Overwrite = false,
    // ContactPolicy T7.2 (aditivo, default ambos true): el cliente puede recibir por cada canal.
    bool EmailEnabled = true,
    bool SmsEnabled = true
);
