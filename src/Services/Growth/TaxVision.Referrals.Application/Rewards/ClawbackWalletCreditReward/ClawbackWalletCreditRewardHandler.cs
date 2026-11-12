using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Application.Common;
using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Referrals.Application.Rewards.ClawbackWalletCreditReward;

/// <summary>
/// El pago del referido fue reembolsado o disputado: el premio del referidor deja de corresponder.
/// <paramref name="RefundedCents"/> es el total reembolsado del pago (null = pérdida total, p. ej. un chargeback
/// perdido). Un reembolso PARCIAL no revierte el premio: solo el total.
/// </summary>
public sealed record ClawbackWalletCreditRewardCommand(
    Guid PaymentId,
    long? RefundedCents,
    string Reason,
    Guid ActorUserId
);

/// <summary>El Wallet respondió sobre la reversa (aplicada, o «no había crédito que revertir»): se cierra el caso.</summary>
public sealed record ConfirmWalletReversalCommand(Guid TenantId, Guid RewardCaseId, Guid ActorUserId);

public enum ConfirmWalletReversalOutcome
{
    Reversed,
    AlreadyReversed,
}

/// <summary>Resultado del clawback: qué pasó y, si hay que pedir la reversa al Wallet, la solicitud a publicar.</summary>
public sealed record ClawbackWalletCreditRewardResult(
    ClawbackWalletCreditRewardOutcome Outcome,
    WalletCreditRequest? Request
);

public enum ClawbackWalletCreditRewardOutcome
{
    /// <summary>El pago no generó ningún premio (sin referido, o no calificó).</summary>
    NoReward,

    /// <summary>Reembolso parcial: el premio se mantiene.</summary>
    PartialRefundIgnored,

    /// <summary>El premio aún no se había acreditado: se canceló sin tocar el Wallet.</summary>
    Cancelled,

    /// <summary>
    /// El premio ya acreditado quedó en <c>ClawbackPending</c> y se PIDIÓ la reversa al Wallet por evento; el caso pasa a
    /// <c>Reversed</c> cuando el Wallet responde (<see cref="ConfirmWalletReversalHandler"/>).
    /// </summary>
    ReversalRequested,

    /// <summary>El caso ya estaba cancelado/revertido/fallido: nada que hacer (reentrega del evento).</summary>
    AlreadyHandled,
}

/// <summary>
/// Revierte el premio de un pago reembolsado. Mismo esquema por EVENTO que el crédito (sin M2M): se guarda el estado y se
/// PIDE la reversa al Wallet; su respuesta cierra el caso. Es idempotente por referencia, así que reentregar es seguro.
/// <list type="bullet">
/// <item><c>Requested</c> → se cancela (nada se acreditó).</item>
/// <item><c>PendingGrant</c>/<c>Granted</c>/<c>Vested</c> → <c>ClawbackPending</c>, reversa en el Wallet y <c>Reversed</c>.
/// Si el Wallet dice que no hay crédito que revertir (el crédito nunca se hizo), el caso igual queda revertido.</item>
/// </list>
/// </summary>
public static class ClawbackWalletCreditRewardHandler
{
    internal static WalletCreditRequest ReversalRequest(ReferralRewardCase reward) =>
        new(
            reward.TenantId,
            reward.Id,
            reward.RewardAmountCents,
            reward.RewardCurrency!,
            "Referral reward reversed: the referred payment was refunded",
            Reverse: true
        );

    public static async Task<Result<ClawbackWalletCreditRewardResult>> Handle(
        ClawbackWalletCreditRewardCommand command,
        IReferralRewardCaseRepository rewards,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILogger<ClawbackWalletCreditRewardCommand> logger,
        CancellationToken ct
    )
    {
        var actor = ApplicationGuards.EnsureActor(command.ActorUserId);
        if (actor.IsFailure)
            return Result.Failure<ClawbackWalletCreditRewardResult>(actor.Error);

        // Evento de sistema: si el mensaje no trae un tenant en contexto, se actúa como plataforma para el lookup por pago.
        if (!tenantContext.HasTenant)
            tenantContext.SetTenant(PlatformTenant.Id);

        var reward = await rewards.GetByPaymentAsync(command.PaymentId, ct);
        if (reward is null || reward.RewardType != ReferralRewardType.WalletCredit)
            return Result.Success(
                new ClawbackWalletCreditRewardResult(ClawbackWalletCreditRewardOutcome.NoReward, null)
            );

        if (command.RefundedCents is { } refunded && refunded < reward.PricePaidCents)
            return Result.Success(
                new ClawbackWalletCreditRewardResult(ClawbackWalletCreditRewardOutcome.PartialRefundIgnored, null)
            );

        // El premio pertenece al tenant referidor: las escrituras siguientes se hacen en SU contexto.
        tenantContext.SetTenant(reward.TenantId);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        switch (reward.Status)
        {
            case ReferralRewardCaseStatus.Cancelled
            or ReferralRewardCaseStatus.Reversed
            or ReferralRewardCaseStatus.Failed:
                return Result.Success(
                    new ClawbackWalletCreditRewardResult(ClawbackWalletCreditRewardOutcome.AlreadyHandled, null)
                );

            case ReferralRewardCaseStatus.Requested:
            {
                var cancelled = reward.Cancel(command.Reason, command.ActorUserId, nowUtc);
                if (cancelled.IsFailure)
                    return Result.Failure<ClawbackWalletCreditRewardResult>(cancelled.Error);

                await unitOfWork.SaveChangesAsync(ct);
                logger.LogInformation(
                    "Reward {RewardCaseId} cancelled before crediting: payment {PaymentId} was refunded.",
                    reward.Id,
                    command.PaymentId
                );
                return Result.Success(
                    new ClawbackWalletCreditRewardResult(ClawbackWalletCreditRewardOutcome.Cancelled, null)
                );
            }

            case ReferralRewardCaseStatus.ClawbackPending:
                break; // se retoma la reversa abajo

            default:
            {
                var requested = reward.RequestClawback(command.Reason, command.ActorUserId, nowUtc);
                if (requested.IsFailure)
                    return Result.Failure<ClawbackWalletCreditRewardResult>(requested.Error);

                await unitOfWork.SaveChangesAsync(ct);
                break;
            }
        }

        // Se pide la reversa al Wallet por evento. El caso queda ClawbackPending hasta que el Wallet responda; si aún
        // no responde, el worker la vuelve a pedir pasado un rato (el Wallet es idempotente por referencia).
        if (reward.Status == ReferralRewardCaseStatus.ClawbackPending)
        {
            var noted = reward.NoteWalletRetry(command.ActorUserId, nowUtc);
            if (noted.IsSuccess)
                await unitOfWork.SaveChangesAsync(ct);
        }

        logger.LogInformation(
            "Wallet reversal requested for reward {RewardCaseId} (tenant {TenantId}): payment {PaymentId} was refunded.",
            reward.Id,
            reward.TenantId,
            command.PaymentId
        );
        return Result.Success(
            new ClawbackWalletCreditRewardResult(
                ClawbackWalletCreditRewardOutcome.ReversalRequested,
                ReversalRequest(reward)
            )
        );
    }
}

/// <summary>
/// Pide de nuevo al Wallet la reversa de un caso que sigue en <c>ClawbackPending</c> sin respuesta (lo dispara el worker).
/// </summary>
public sealed record ResendWalletReversalCommand(Guid TenantId, Guid RewardCaseId, Guid ActorUserId);

public static class ResendWalletReversalHandler
{
    public static async Task<Result<WalletCreditRequest?>> Handle(
        ResendWalletReversalCommand command,
        IReferralRewardCaseRepository rewards,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        var actor = ApplicationGuards.EnsureActor(command.ActorUserId);
        if (actor.IsFailure)
            return Result.Failure<WalletCreditRequest?>(actor.Error);

        var reward = await rewards.GetByIdAsync(command.RewardCaseId, command.TenantId, ct);
        if (reward is null || reward.Status != ReferralRewardCaseStatus.ClawbackPending)
            return Result.Success<WalletCreditRequest?>(null); // ya se resolvió: nada que reintentar

        var noted = reward.NoteWalletRetry(command.ActorUserId, timeProvider.GetUtcNow().UtcDateTime);
        if (noted.IsFailure)
            return Result.Failure<WalletCreditRequest?>(noted.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success<WalletCreditRequest?>(ClawbackWalletCreditRewardHandler.ReversalRequest(reward));
    }
}

/// <summary>
/// El Wallet respondió sobre la reversa (aplicada, o «no había crédito que revertir»): el caso queda <c>Reversed</c> y el
/// beneficio registrado se marca revertido. Idempotente: reentregar el evento no duplica nada.
/// </summary>
public static class ConfirmWalletReversalHandler
{
    public static async Task<Result<ConfirmWalletReversalOutcome>> Handle(
        ConfirmWalletReversalCommand command,
        IReferralRewardCaseRepository rewards,
        IReferrerBenefitRepository benefits,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILogger<ConfirmWalletReversalCommand> logger,
        CancellationToken ct
    )
    {
        var actor = ApplicationGuards.EnsureActor(command.ActorUserId);
        if (actor.IsFailure)
            return Result.Failure<ConfirmWalletReversalOutcome>(actor.Error);

        var reward = await rewards.GetByIdAsync(command.RewardCaseId, command.TenantId, ct);
        if (reward is null)
        {
            return Result.Failure<ConfirmWalletReversalOutcome>(
                new Error("ReferralReward.NotFound", "The referral reward does not exist.")
            );
        }

        if (reward.Status == ReferralRewardCaseStatus.Reversed)
            return Result.Success(ConfirmWalletReversalOutcome.AlreadyReversed);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var reversed = reward.ConfirmReversed(command.ActorUserId, nowUtc);
        if (reversed.IsFailure)
            return Result.Failure<ConfirmWalletReversalOutcome>(reversed.Error);

        var benefit = await benefits.GetByRewardCaseAsync(reward.Id, reward.TenantId, ct);
        benefit?.MarkReversed(nowUtc);

        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation(
            "Reward {RewardCaseId} reversed in the wallet of tenant {TenantId}.",
            reward.Id,
            reward.TenantId
        );
        return Result.Success(ConfirmWalletReversalOutcome.Reversed);
    }
}
