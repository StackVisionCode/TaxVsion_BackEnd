using TaxVision.Referrals.Application.Programs.CreateTenantReferralProgram;

namespace TaxVision.Referrals.Application.Programs.UpdateTenantReferralProgramPolicy;

/// <summary>Cambia la política del programa (nueva versión). Los campos omitidos conservan su valor actual.</summary>
public sealed record UpdateTenantReferralProgramPolicyCommand(
    Guid ProgramId,
    TenantReferralProgramPolicyInput Policy,
    Guid ActorUserId,
    string IdempotencyKey
);
