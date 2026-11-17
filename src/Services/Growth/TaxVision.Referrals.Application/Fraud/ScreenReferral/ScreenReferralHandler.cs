using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Domain.Attributions;
using TaxVision.Referrals.Domain.Fraud;
using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Referrals.Application.Fraud.ScreenReferral;

/// <summary>
/// Compara la oficina referida por un alta con su referidor. Corre al enlazar la oficina y al conocerse el email de su
/// dueño (lo que llegue último decide). Idempotente: una atribución ya rechazada o en revisión no se vuelve a evaluar.
/// </summary>
public sealed record ScreenReferralCommand(Guid OnboardingId);

/// <param name="RewardTenantId">Oficina dueña del premio (el referidor), para avisarle si se canceló.</param>
public sealed record ReferralScreeningResult(
    ReferralFraudVerdict Verdict,
    string? Signal,
    Guid? RewardCaseId,
    Guid? RewardTenantId,
    bool RewardCancelled
)
{
    public static readonly ReferralScreeningResult Clear = new(ReferralFraudVerdict.Clear, null, null, null, false);
}

public static class ScreenReferralHandler
{
    // Las decisiones automáticas las firma el sistema.
    private static readonly Guid SystemActor = PlatformTenant.Id;

    public static async Task<Result<ReferralScreeningResult>> Handle(
        ScreenReferralCommand command,
        IReferralAttributionRepository attributions,
        IReferralRewardCaseRepository rewards,
        IReferralFraudReviewRepository reviews,
        IOfficeOwnerSignalsReader owners,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        if (command.OnboardingId == Guid.Empty)
            return Result.Success(ReferralScreeningResult.Clear);

        // La atribución del alta es del tenant del alta.
        tenantContext.SetTenant(command.OnboardingId);
        var attribution = await attributions.GetByRefereeAsync(command.OnboardingId, command.OnboardingId, ct);
        if (attribution?.RefereeTenantId is not { } office || !IsOpen(attribution))
            return Result.Success(ReferralScreeningResult.Clear);

        var finding = ReferralFraudScreen.Evaluate(
            attribution,
            await owners.ForOfficeAsync(office, ct),
            await owners.ForOfficeAsync(attribution.ReferrerId, ct)
        );
        if (finding.Verdict == ReferralFraudVerdict.Clear)
            return Result.Success(ReferralScreeningResult.Clear);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var reward = await rewards.GetByAttributionAsync(attribution.Id, ct);
        var decided =
            finding.Verdict == ReferralFraudVerdict.Block
                ? Block(attribution, reward, nowUtc)
                : Hold(attribution, reward, finding.Signal!, nowUtc);
        if (decided.IsFailure)
            return Result.Failure<ReferralScreeningResult>(decided.Error);

        var recorded = await RecordReviewAsync(attribution, reward, finding, reviews, tenantContext, nowUtc, ct);
        if (recorded.IsFailure)
            return Result.Failure<ReferralScreeningResult>(recorded.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(
            new ReferralScreeningResult(
                finding.Verdict,
                finding.Signal,
                reward?.Id,
                reward?.TenantId,
                reward?.Status == ReferralRewardCaseStatus.Cancelled
            )
        );
    }

    private static bool IsOpen(ReferralAttribution attribution) =>
        attribution.Status is ReferralAttributionStatus.Active or ReferralAttributionStatus.Qualified;

    // Mismo usuario: la atribución se rechaza y el premio aún no acreditado se cancela.
    private static Result Block(ReferralAttribution attribution, ReferralRewardCase? reward, DateTime nowUtc)
    {
        var rejected = attribution.Reject("SelfReferral", SystemActor, nowUtc);
        if (rejected.IsFailure || reward is null)
            return rejected;

        // Un premio ya acreditado no se toca aquí: queda la revisión registrada para el admin.
        if (reward.Status is ReferralRewardCaseStatus.Requested or ReferralRewardCaseStatus.ManualReview)
            return reward.Cancel("SelfReferral", SystemActor, nowUtc);
        return Result.Success();
    }

    // Email igual o desechable: la atribución queda en revisión y el premio retenido.
    private static Result Hold(
        ReferralAttribution attribution,
        ReferralRewardCase? reward,
        string signal,
        DateTime nowUtc
    )
    {
        var opened = attribution.OpenFraudReview(SystemActor, nowUtc);
        if (opened.IsFailure || reward?.Status != ReferralRewardCaseStatus.Requested)
            return opened;

        return reward.FlagForReview($"Fraud review: {signal}", SystemActor, nowUtc);
    }

    private static async Task<Result> RecordReviewAsync(
        ReferralAttribution attribution,
        ReferralRewardCase? reward,
        ReferralFraudFinding finding,
        IReferralFraudReviewRepository reviews,
        ITenantContext tenantContext,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var key = $"fraud-screen:{attribution.Id:N}";
        var opened = ReferralFraudReview.Open(
            attribution.ProgramId,
            attribution.TenantScopeId,
            attribution.ReferrerId,
            attribution.Id,
            reward?.Id,
            finding.Signal!,
            $"signal:{finding.Signal}",
            key,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key))),
            SystemActor,
            nowUtc
        );
        if (opened.IsFailure)
            return opened;

        var review = opened.Value;
        if (finding.Verdict == ReferralFraudVerdict.Block)
        {
            var closed = review.Reject("Automatic: the referrer user completed the signup.", SystemActor, nowUtc);
            if (closed.IsFailure)
                return closed;
        }

        // La revisión es de la oficina referidora (o del alcance del programa).
        tenantContext.SetTenant(review.TenantId);
        await reviews.AddAsync(review, ct);
        return Result.Success();
    }
}
