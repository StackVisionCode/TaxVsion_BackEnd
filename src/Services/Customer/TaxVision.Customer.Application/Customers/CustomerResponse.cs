using TaxVision.Customer.Domain.Customers;

namespace TaxVision.Customer.Application.Customers;

public sealed record CustomerResponse(
    Guid Id,
    Guid TenantId,
    CustomerKind Kind,
    CustomerStatus Status,
    string DisplayName,
    string PrimaryEmail,
    string? PrimaryPhone,
    Language Language,
    Guid? OccupationId,
    string? OccupationName,
    Guid? PrincipalBusinessActivityId,
    string? PrincipalBusinessActivityName,
    DateTime CreatedAtUtc,
    Guid? AssignedPreparerUserId = null,
    // ContactPolicy T7.2 — default true/true para respuestas de clientes previos a la migración.
    bool EmailEnabled = true,
    bool SmsEnabled = true,
    // Foto vigente (null = iniciales). URL: /storage/avatars/{AvatarFileId}.
    Guid? AvatarFileId = null
);
