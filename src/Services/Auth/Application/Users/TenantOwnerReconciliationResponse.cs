namespace TaxVision.Auth.Application.Users;

public sealed record TenantOwnerReconciliationResponse(
    Guid TenantId,
    Guid UserId,
    DateTime RegisteredAtUtc,
    bool IsActive
);
