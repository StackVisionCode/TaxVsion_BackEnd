using TaxVision.Customer.Application.Customers.FiscalProfiles;
using TaxVision.Customer.Domain.Customers;

namespace TaxVision.Customer.Application.Customers;

/// <summary>
/// Proyección de LECTURA del cliente completo para la ficha de detalle (GET /customers/{id}).
/// Superset de <see cref="CustomerResponse"/>: mismos escalares + las sub-colecciones que hasta
/// ahora eran write-only (solo salían como retorno del POST/PATCH que las creaba). El identificador
/// fiscal viaja SIEMPRE enmascarado (last4); el completo se queda en el endpoint auditado de reveal.
/// </summary>
public sealed record CustomerDetailResponse(
    Guid Id,
    Guid TenantId,
    CustomerKind Kind,
    CustomerStatus Status,
    string DisplayName,
    // Partes del nombre (write-only hasta ahora): sin ellas el form de edición partía el DisplayName
    // de forma lossy y, con el merge parcial del PATCH, corrompía el nombre. Solo proyección.
    string? FirstName,
    string? MiddleName,
    string? LastName,
    string? LegalName,
    string PrimaryEmail,
    string? PrimaryPhone,
    Language Language,
    PreferredChannel PreferredChannel,
    Guid? OccupationId,
    string? OccupationName,
    Guid? PrincipalBusinessActivityId,
    string? PrincipalBusinessActivityName,
    // Fecha de nacimiento (individuo). Se persiste desde el alta/edición pero hasta ahora no se
    // devolvía en el detalle, así que la ficha la pintaba en blanco. Solo proyección de lectura.
    DateOnly? DateOfBirth,
    DateTime CreatedAtUtc,
    Guid? AssignedPreparerUserId,
    IReadOnlyList<AddressResponse> Addresses,
    IReadOnlyList<ContactPointResponse> ContactPoints,
    IReadOnlyList<RelationResponse> Relations,
    CustomerFiscalProfileResponse? FiscalProfile
);
