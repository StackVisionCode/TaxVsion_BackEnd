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
    PreferredChannel PreferredChannel,
    Guid? OccupationId,
    string? OccupationName,
    Guid? PrincipalBusinessActivityId,
    string? PrincipalBusinessActivityName,
    DateTime CreatedAtUtc
);
