using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Referrals.Domain.Common;

namespace TaxVision.Referrals.Domain.Rewards;

/// <summary>
/// Registro inmutable de un beneficio acreditado al Wallet del referidor — el
/// <c>ReferrerBenefits{tenantId, %, totalBenefitWallet, UsuarioSistema, Fecha}</c> del esquema del
/// dueño. Se crea al confirmar el crédito (<see cref="ReferralRewardCase"/> → Granted). El
/// acumulado "Total ganado" de un tenant es la suma de <see cref="TotalBenefitWalletCents"/> de sus
/// registros no revertidos.
/// </summary>
public sealed class ReferrerBenefit : TenantEntity
{
    public Guid RewardCaseId { get; private set; }
    public Guid AttributionId { get; private set; }

    /// <summary>Porcentaje aplicado, en puntos básicos (snapshot del caso de premio).</summary>
    public int AppliedBasisPoints { get; private set; }

    /// <summary>Monto acreditado al Wallet por este beneficio, en centavos.</summary>
    public long TotalBenefitWalletCents { get; private set; }
    public string Currency { get; private set; } = default!;

    /// <summary>Quién ejecutó el crédito: el actor de sistema (worker), nunca un usuario de la oficina.</summary>
    [UserReference(UserReferenceKind.Provenance)]
    public Guid SystemActorId { get; private set; }
    public DateTime CreditedAtUtc { get; private set; }

    /// <summary>Marca de reversión (clawback confirmado); el registro no se borra.</summary>
    public DateTime? ReversedAtUtc { get; private set; }

    private ReferrerBenefit() { }

    public static Result<ReferrerBenefit> Record(
        ReferralRewardCase rewardCase,
        Guid systemActorId,
        DateTime nowUtc
    )
    {
        var actor = DomainGuards.EnsureActor(systemActorId);
        if (actor.IsFailure)
            return Result.Failure<ReferrerBenefit>(actor.Error);

        if (rewardCase.RewardType != ReferralRewardType.WalletCredit || rewardCase.RewardAmountCents <= 0)
        {
            return Result.Failure<ReferrerBenefit>(
                new Error(
                    "ReferrerBenefit.NotMonetary",
                    "Only a WalletCredit reward with a positive amount produces a referrer benefit."
                )
            );
        }

        var benefit = new ReferrerBenefit
        {
            RewardCaseId = rewardCase.Id,
            AttributionId = rewardCase.AttributionId,
            AppliedBasisPoints = rewardCase.RewardBasisPoints,
            TotalBenefitWalletCents = rewardCase.RewardAmountCents,
            Currency = rewardCase.RewardCurrency!,
            SystemActorId = systemActorId,
            CreditedAtUtc = nowUtc,
        };
        benefit.SetTenant(rewardCase.TenantId);
        return Result.Success(benefit);
    }

    public Result MarkReversed(DateTime nowUtc)
    {
        if (ReversedAtUtc is not null)
            return Result.Success();

        ReversedAtUtc = nowUtc;
        return Result.Success();
    }
}
