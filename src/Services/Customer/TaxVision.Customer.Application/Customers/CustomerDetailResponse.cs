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
    CustomerFiscalProfileResponse? FiscalProfile,
    // Staff asignado (M:N) con su rol (IsPrimary = responsable) — para el diálogo de asignación.
    IReadOnlyList<CustomerAssigneeResponse> Assignees,
    // ContactPolicy T7.2 — default true/true (opt-out). Lo pinta el form de edición del CRM.
    bool EmailEnabled = true,
    bool SmsEnabled = true,
    // Foto vigente (null = iniciales) y, si hay una subida en curso, su id y estado
    // ("processing" | "failed"; "failed" = pendiente vencida). URL: /storage/avatars/{id}.
    Guid? AvatarFileId = null,
    Guid? PendingAvatarFileId = null,
    string? AvatarUploadStatus = null,
    // T-20: "under_review" | "blocked" para AvatarModerationFileId (la última foto subida), o null.
    string? AvatarModerationStatus = null,
    Guid? AvatarModerationFileId = null
);
