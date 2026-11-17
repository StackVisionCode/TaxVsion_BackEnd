using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Referrals.Application.Rewards.DisputeHold;

/// <summary>Se abrió una disputa del pago del referido: el premio aún sin acreditar se retiene hasta el cierre.</summary>
public sealed record HoldReferralRewardForDisputeCommand(Guid PaymentId, string DisputeId, Guid ActorUserId);

/// <summary>La disputa del pago se ganó: el premio retenido por ella vuelve a esperar su acreditación.</summary>
public sealed record ReleaseReferralRewardDisputeHoldCommand(Guid PaymentId, string DisputeId, Guid ActorUserId);

public enum ReferralRewardDisputeHoldOutcome
{
    /// <summary>El pago no generó premio.</summary>
    NoReward,

    /// <summary>El premio quedó retenido por la disputa.</summary>
    Held,

    /// <summary>El premio ya se estaba acreditando o se acreditó: se decide al cierre (perdida = clawback).</summary>
    AlreadyCredited,

    /// <summary>El premio ya estaba en revisión por otro motivo: sigue igual.</summary>
    AlreadyInReview,

    /// <summary>La disputa ganada liberó el premio.</summary>
    Released,

    /// <summary>No lo retenía esta disputa (revisión del admin, o nunca se retuvo): nada que liberar.</summary>
    NotHeldByThisDispute,
}

public static class HoldReferralRewardForDisputeHandler
{
    public static async Task<Result<ReferralRewardDisputeHoldOutcome>> Handle(
        HoldReferralRewardForDisputeCommand command,
        IReferralRewardCaseRepository rewards,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILogger<HoldReferralRewardForDisputeCommand> logger,
        CancellationToken ct
    )
    {
        var reward = await ReferralRewardByPayment.FindAsync(command.PaymentId, rewards, tenantContext, ct);
        if (reward is null)
            return Result.Success(ReferralRewardDisputeHoldOutcome.NoReward);

        var held = reward.HoldForDispute(command.DisputeId, command.ActorUserId, timeProvider.GetUtcNow().UtcDateTime);
        if (held.IsFailure)
            return Result.Failure<ReferralRewardDisputeHoldOutcome>(held.Error);

        if (!held.Value)
            return Result.Success(
                reward.Status == ReferralRewardCaseStatus.ManualReview
                    ? ReferralRewardDisputeHoldOutcome.AlreadyInReview
                    : ReferralRewardDisputeHoldOutcome.AlreadyCredited
            );

        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation(
            "Reward {RewardCaseId} held while payment {PaymentId} is disputed ({DisputeId}).",
            reward.Id,
            command.PaymentId,
            command.DisputeId
        );
        return Result.Success(ReferralRewardDisputeHoldOutcome.Held);
    }
}

public static class ReleaseReferralRewardDisputeHoldHandler
{
    public static async Task<Result<ReferralRewardDisputeHoldOutcome>> Handle(
        ReleaseReferralRewardDisputeHoldCommand command,
        IReferralRewardCaseRepository rewards,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILogger<ReleaseReferralRewardDisputeHoldCommand> logger,
        CancellationToken ct
    )
    {
        var reward = await ReferralRewardByPayment.FindAsync(command.PaymentId, rewards, tenantContext, ct);
        if (reward is null)
            return Result.Success(ReferralRewardDisputeHoldOutcome.NoReward);

        var released = reward.ReleaseDisputeHold(
            command.DisputeId,
            command.ActorUserId,
            timeProvider.GetUtcNow().UtcDateTime
        );
        if (released.IsFailure)
            return Result.Failure<ReferralRewardDisputeHoldOutcome>(released.Error);

        if (!released.Value)
            return Result.Success(ReferralRewardDisputeHoldOutcome.NotHeldByThisDispute);

        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation(
            "Reward {RewardCaseId} released: the dispute {DisputeId} of payment {PaymentId} was won.",
            reward.Id,
            command.DisputeId,
            command.PaymentId
        );
        return Result.Success(ReferralRewardDisputeHoldOutcome.Released);
    }
}

/// <summary>Busca el premio por pago (como plataforma si no hay tenant) y deja el contexto en el tenant referidor.</summary>
internal static class ReferralRewardByPayment
{
    public static async Task<ReferralRewardCase?> FindAsync(
        Guid paymentId,
        IReferralRewardCaseRepository rewards,
        ITenantContext tenantContext,
        CancellationToken ct
    )
    {
        if (!tenantContext.HasTenant)
            tenantContext.SetTenant(PlatformTenant.Id);

        var reward = await rewards.GetByPaymentAsync(paymentId, ct);
        if (reward is null || reward.RewardType != ReferralRewardType.WalletCredit)
            return null;

        tenantContext.SetTenant(reward.TenantId);
        return reward;
    }
}
