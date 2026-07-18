using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Users;

public sealed record UserResponse(
    Guid Id,
    Guid TenantId,
    string Name,
    string LastName,
    string Email,
    UserActorType ActorType,
    Guid? CustomerId
);
