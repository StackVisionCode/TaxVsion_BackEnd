namespace TaxVision.Auth.Application.Users;

public sealed record StaffUserReconciliationResponse(
    Guid TenantId,
    Guid UserId,
    string ActorType,
    bool IsActive,
    bool IsOffboarded
);
