using TaxVision.Customer.Domain.Customers;

namespace TaxVision.Customer.Application.Customers.Commands.Create;

public sealed record CreateCustomerCommand(
    Guid TenantId,
    Guid CreatedByUserId,
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
    PreferredChannel PreferredChannel,
    /// <summary>
    /// Qué hacer si ya existe uno igual. En false —el valor por defecto— se responde 409 con el id del
    /// que ya está, y decide quien llama; en true se le aplican encima los datos nuevos.
    /// </summary>
    bool Overwrite = false
);
