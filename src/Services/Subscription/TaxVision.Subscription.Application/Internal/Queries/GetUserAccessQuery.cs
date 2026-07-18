namespace TaxVision.Subscription.Application.Internal.Queries;

public sealed record GetUserAccessQuery(Guid TenantId, Guid UserId);
