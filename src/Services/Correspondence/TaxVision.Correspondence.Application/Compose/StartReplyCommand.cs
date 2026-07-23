namespace TaxVision.Correspondence.Application.Compose;

public sealed record StartReplyCommand(Guid TenantId, Guid IncomingEmailId, Guid AccountId, Guid ActorId);
