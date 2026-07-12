using TaxVision.Customer.Domain.Customers;

namespace TaxVision.Customer.Application.Customers.Commands.Update;

/// <summary>
/// Comando de update parcial (patron PATCH). Todos los campos son opcionales.
/// El handler solo aplica el cambio si el campo viene con valor:
///   - Para strings: no vacio.
///   - Para tipos nullable: HasValue == true (o no null en el caso de PrimaryPhone).
///
/// Los campos de identidad (PersonalName / BusinessIdentity) se envian juntos como grupo:
/// si mandas FirstName debes mandar tambien LastName. El aggregate rechaza combinaciones invalidas.
/// </summary>
public sealed record UpdateCustomerCommand(
    Guid CustomerId,
    Guid ModifiedByUserId,
    // Preferencias y contacto
    Language Language,
    PreferredChannel PreferredChannel,
    Guid? OccupationId,
    Guid? ProfilePictureFileId,
    string PrimaryEmail,
    string? PrimaryPhone,
    // ==== NUEVO: identidad editable ====
    // Individual
    string? FirstName,
    string? MiddleName,
    string? LastName,
    string? Prefix,
    string? Suffix,
    DateOnly? DateOfBirth,
    // Business
    string? LegalName,
    string? Dba,
    BusinessStructure? BusinessStructure,
    DateOnly? FormationDate,
    Guid? PrincipalBusinessActivityId
);
