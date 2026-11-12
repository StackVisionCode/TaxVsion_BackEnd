namespace TaxVision.Referrals.Application.Programs.SuspendTenantReferralProgram;

public sealed record SuspendTenantReferralProgramCommand(Guid ProgramId, Guid ActorUserId, string IdempotencyKey);
