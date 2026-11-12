using BuildingBlocks.Jobs;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Growth.Infrastructure.Messaging;
using TaxVision.Growth.Infrastructure.Persistence;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Application.Rewards.ClawbackWalletCreditReward;
using TaxVision.Referrals.Application.Rewards.GrantWalletCreditReward;
using Wolverine;

namespace TaxVision.Growth.Infrastructure.Scheduling;

/// <summary>
/// PIDE al Wallet (por evento, sin M2M) acreditar los premios del referidor cuya espera ya venció. Cada minuto escanea los
/// casos <c>WalletCredit</c> elegibles (<c>Requested</c>, o <c>PendingGrant</c> sin respuesta hace rato) y, por cada uno,
/// ejecuta <see cref="GrantWalletCreditRewardHandler"/> en una unidad transaccional (BB-3) con el tenant del referidor en
/// contexto: el cambio de estado y la solicitud al Wallet se confirman JUNTOS por el outbox, o no queda ninguno.
/// También vuelve a pedir las reversas (<c>ClawbackPending</c>) que llevan rato sin respuesta. La respuesta del Wallet
/// la procesa un consumer, no este worker.
///
/// Resumible e idempotente: el Wallet deduplica por referencia, así que reintentar nunca acredita dos veces. Una unidad
/// por caso: un conflicto o un error en uno no arrastra a los demás.
/// La espera inicial evita correr antes de que el host termine de arrancar (Wolverine/DbContext).
/// </summary>
public sealed class ReferralRewardGrantWorker(IServiceScopeFactory scopeFactory, ILogger<ReferralRewardGrantWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private const int BatchSize = 100;

    // Actor de sistema (no vacío): la auditoría del caso/intento registra al worker, nunca a un usuario de la oficina.
    private static readonly Guid SystemActor = PlatformTenant.Id;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Referral reward grant cycle failed; will retry next tick.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        IReadOnlyList<GrantableRewardCaseRef> due;
        IReadOnlyList<GrantableRewardCaseRef> staleReversals;
        await using (var scanScope = scopeFactory.CreateAsyncScope())
        {
            var rewards = scanScope.ServiceProvider.GetRequiredService<IReferralRewardCaseRepository>();
            due = await rewards.GetGrantableWalletCreditCasesAsync(DateTime.UtcNow, BatchSize, ct);
            staleReversals = await rewards.GetStaleClawbackPendingCasesAsync(DateTime.UtcNow, BatchSize, ct);
        }

        if (due.Count > 0)
        {
            var summary = await TransactionalJobUnit.RunEachAsync<GrantableRewardCaseRef, GrowthDbContext>(
                scopeFactory,
                due,
                async (item, unit, token) =>
                {
                    var sp = unit.Services;
                    sp.GetRequiredService<ITenantContext>().SetTenant(item.TenantId);

                    var result = await GrantWalletCreditRewardHandler.Handle(
                        new GrantWalletCreditRewardCommand(item.TenantId, item.RewardCaseId, SystemActor),
                        sp.GetRequiredService<IReferralRewardCaseRepository>(),
                        sp.GetRequiredService<IReferralRewardAttemptRepository>(),
                        sp.GetRequiredService<IUnitOfWork>(),
                        sp.GetRequiredService<TimeProvider>(),
                        sp.GetRequiredService<ILogger<GrantWalletCreditRewardCommand>>(),
                        token
                    );
                    if (result.IsFailure)
                    {
                        logger.LogWarning(
                            "Reward {RewardCaseId} (tenant {TenantId}) not requested this cycle: {Error}",
                            item.RewardCaseId,
                            item.TenantId,
                            result.Error.Code
                        );
                        return false;
                    }

                    if (result.Value.Request is { } request)
                        await unit.Outbox.PublishAsync(WalletCreditRequestEvents.ToEvent(request));

                    return true;
                },
                logger,
                ct
            );

            logger.LogInformation(
                "Referral reward cycle requested {Committed}/{Total} credit(s) ({Conflicts} conflict(s), {Failed} failed).",
                summary.Committed,
                due.Count,
                summary.Conflicts,
                summary.Failed
            );
        }

        if (staleReversals.Count > 0)
        {
            await TransactionalJobUnit.RunEachAsync<GrantableRewardCaseRef, GrowthDbContext>(
                scopeFactory,
                staleReversals,
                async (item, unit, token) =>
                {
                    var sp = unit.Services;
                    sp.GetRequiredService<ITenantContext>().SetTenant(item.TenantId);

                    var result = await ResendWalletReversalHandler.Handle(
                        new ResendWalletReversalCommand(item.TenantId, item.RewardCaseId, SystemActor),
                        sp.GetRequiredService<IReferralRewardCaseRepository>(),
                        sp.GetRequiredService<IUnitOfWork>(),
                        sp.GetRequiredService<TimeProvider>(),
                        token
                    );
                    if (result.IsFailure)
                    {
                        logger.LogWarning(
                            "Reversal of reward {RewardCaseId} (tenant {TenantId}) not re-requested: {Error}",
                            item.RewardCaseId,
                            item.TenantId,
                            result.Error.Code
                        );
                        return false;
                    }

                    if (result.Value is not { } request)
                        return false; // ya se resolvió

                    await unit.Outbox.PublishAsync(WalletCreditRequestEvents.ToEvent(request));
                    return true;
                },
                logger,
                ct
            );
        }
    }
}
