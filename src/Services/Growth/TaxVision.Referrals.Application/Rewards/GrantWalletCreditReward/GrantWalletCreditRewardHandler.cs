using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Application.Common;
using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Referrals.Application.Rewards.GrantWalletCreditReward;

/// <summary>
/// Pide al Wallet acreditar el premio del referidor, por EVENTO (sin M2M), en dos pasos:
/// <list type="number">
/// <item>Aquí: el caso pasa de <c>Requested</c> a <c>PendingGrant</c> (la espera ya venció), se abre el intento y se
/// DEVUELVE la solicitud de crédito (referencia opaca <c>referral-reward</c> + id del caso) para que quien invoca la publique.</item>
/// <item>Cuando el Wallet responde, <see cref="ConfirmWalletCreditRewardHandler"/> marca el caso <c>Granted</c> y registra
/// el <see cref="ReferrerBenefit"/>.</item>
/// </list>
/// Resumible: un caso <c>PendingGrant</c> sin respuesta vuelve a pedirse (el worker lo hace pasado un rato) y el Wallet
/// deduplica por referencia, así que nunca hay doble crédito.
/// </summary>
public static class GrantWalletCreditRewardHandler
{
    public static async Task<Result<GrantWalletCreditRewardResult>> Handle(
        GrantWalletCreditRewardCommand command,
        IReferralRewardCaseRepository rewards,
        IReferralRewardAttemptRepository attempts,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILogger<GrantWalletCreditRewardCommand> logger,
        CancellationToken ct
    )
    {
        var actor = ApplicationGuards.EnsureActor(command.ActorUserId);
        if (actor.IsFailure)
            return Result.Failure<GrantWalletCreditRewardResult>(actor.Error);

        var reward = await rewards.GetByIdAsync(command.RewardCaseId, command.TenantId, ct);
        if (reward is null)
        {
            return Result.Failure<GrantWalletCreditRewardResult>(
                new Error("ReferralReward.NotFound", "The referral reward does not exist.")
            );
        }

        if (reward.RewardType != ReferralRewardType.WalletCredit || reward.RewardAmountCents <= 0)
        {
            return Result.Failure<GrantWalletCreditRewardResult>(
                new Error("ReferralReward.NotWalletCredit", "The reward is not a wallet credit with a positive amount.")
            );
        }

        if (reward.Status is ReferralRewardCaseStatus.Granted or ReferralRewardCaseStatus.Vested)
            return Result.Success(new GrantWalletCreditRewardResult(GrantWalletCreditRewardOutcome.AlreadyGranted, null));

        var fingerprint = CanonicalPayloadFingerprint.Compute(reward.TenantId, reward.Id);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        if (reward.Status == ReferralRewardCaseStatus.Requested)
        {
            var begin = reward.BeginGrant(command.ActorUserId, nowUtc);
            if (begin.IsFailure)
                return Result.Failure<GrantWalletCreditRewardResult>(begin.Error);

            var started = ReferralRewardAttempt.Start(
                reward,
                ReferralRewardOperation.Grant,
                $"reward-grant:{reward.Id:N}",
                fingerprint,
                command.ActorUserId,
                nowUtc
            );
            if (started.IsFailure)
                return Result.Failure<GrantWalletCreditRewardResult>(started.Error);

            await attempts.AddAsync(started.Value, ct);
        }
        else if (reward.Status == ReferralRewardCaseStatus.PendingGrant)
        {
            // Reintento: se vuelve a pedir; el intento abierto (o uno nuevo si faltara) acompaña la espera de la respuesta.
            if (await attempts.GetOpenGrantAttemptAsync(reward.Id, command.TenantId, ct) is null)
            {
                var restarted = ReferralRewardAttempt.Start(
                    reward,
                    ReferralRewardOperation.Grant,
                    $"reward-grant:{reward.Id:N}:{Guid.NewGuid():N}",
                    fingerprint,
                    command.ActorUserId,
                    nowUtc
                );
                if (restarted.IsFailure)
                    return Result.Failure<GrantWalletCreditRewardResult>(restarted.Error);

                await attempts.AddAsync(restarted.Value, ct);
            }

            var noted = reward.NoteWalletRetry(command.ActorUserId, nowUtc);
            if (noted.IsFailure)
                return Result.Failure<GrantWalletCreditRewardResult>(noted.Error);
        }
        else
        {
            return Result.Failure<GrantWalletCreditRewardResult>(
                new Error(
                    "ReferralReward.InvalidTransition",
                    $"A reward in state {reward.Status} cannot be credited to the wallet."
                )
            );
        }

        // El estado queda guardado ANTES de devolver la solicitud: si quien la publica falla, el caso sigue PendingGrant y se reintenta.
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Wallet credit requested for reward {RewardCaseId} (tenant {TenantId}): {AmountCents} {Currency} cents.",
            reward.Id,
            reward.TenantId,
            reward.RewardAmountCents,
            reward.RewardCurrency
        );
        return Result.Success(
            new GrantWalletCreditRewardResult(
                GrantWalletCreditRewardOutcome.Requested,
                new WalletCreditRequest(
                    reward.TenantId,
                    reward.Id,
                    reward.RewardAmountCents,
                    reward.RewardCurrency!,
                    "Referral reward",
                    Reverse: false
                )
            )
        );
    }
}

/// <summary>
/// El Wallet respondió que acreditó: se cierra el intento, el caso queda <c>Granted</c> y se registra el
/// <see cref="ReferrerBenefit"/>. Idempotente: reentregar el evento no duplica nada.
/// </summary>
public static class ConfirmWalletCreditRewardHandler
{
    public static async Task<Result<ConfirmWalletCreditRewardOutcome>> Handle(
        ConfirmWalletCreditRewardCommand command,
        IReferralRewardCaseRepository rewards,
        IReferralRewardAttemptRepository attempts,
        IReferrerBenefitRepository benefits,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILogger<ConfirmWalletCreditRewardCommand> logger,
        CancellationToken ct
    )
    {
        var actor = ApplicationGuards.EnsureActor(command.ActorUserId);
        if (actor.IsFailure)
            return Result.Failure<ConfirmWalletCreditRewardOutcome>(actor.Error);

        var reward = await rewards.GetByIdAsync(command.RewardCaseId, command.TenantId, ct);
        if (reward is null)
        {
            return Result.Failure<ConfirmWalletCreditRewardOutcome>(
                new Error("ReferralReward.NotFound", "The referral reward does not exist.")
            );
        }

        if (reward.Status is ReferralRewardCaseStatus.Granted or ReferralRewardCaseStatus.Vested)
            return Result.Success(ConfirmWalletCreditRewardOutcome.AlreadyGranted);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var fingerprint = CanonicalPayloadFingerprint.Compute(reward.TenantId, reward.Id);
        var materialized = $"wallet-credit:{command.LedgerEntryId:N}";

        if (await attempts.GetOpenGrantAttemptAsync(reward.Id, command.TenantId, ct) is { } attempt)
        {
            var attemptDone = attempt.MarkSucceeded(
                materialized,
                $"reward-confirm:{reward.Id:N}",
                fingerprint,
                command.ActorUserId,
                nowUtc
            );
            if (attemptDone.IsFailure)
                return Result.Failure<ConfirmWalletCreditRewardOutcome>(attemptDone.Error);
        }

        var granted = reward.ConfirmGranted(materialized, command.ActorUserId, nowUtc);
        if (granted.IsFailure)
            return Result.Failure<ConfirmWalletCreditRewardOutcome>(granted.Error);

        if (await benefits.GetByRewardCaseAsync(reward.Id, command.TenantId, ct) is null)
        {
            var benefit = ReferrerBenefit.Record(reward, command.ActorUserId, nowUtc);
            if (benefit.IsFailure)
                return Result.Failure<ConfirmWalletCreditRewardOutcome>(benefit.Error);

            await benefits.AddAsync(benefit.Value, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Reward {RewardCaseId} credited to the wallet of tenant {TenantId}: {AmountCents} {Currency} cents (ledger entry {LedgerEntryId}).",
            reward.Id,
            reward.TenantId,
            reward.RewardAmountCents,
            reward.RewardCurrency,
            command.LedgerEntryId
        );
        return Result.Success(ConfirmWalletCreditRewardOutcome.Granted);
    }
}
