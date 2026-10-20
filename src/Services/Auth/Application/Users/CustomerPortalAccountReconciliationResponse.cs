namespace TaxVision.Auth.Application.Users;

public sealed record CustomerPortalAccountReconciliationResponse(
    Guid TenantId,
    Guid CustomerId,
    Guid UserId,
    bool IsActive
);
