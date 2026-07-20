namespace TaxVision.Referrals.Application.Programs.ActivateTenantReferralProgram;

public sealed record ActivateTenantReferralProgramCommand(
    Guid ProgramId,
    Guid ActorUserId,
    string IdempotencyKey
);
