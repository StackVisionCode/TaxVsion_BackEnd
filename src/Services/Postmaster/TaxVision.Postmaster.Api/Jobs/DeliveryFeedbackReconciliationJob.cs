using BuildingBlocks.Infrastructure.Hosting;
using Microsoft.Extensions.Options;
using TaxVision.Postmaster.Application.Feedback;
using TaxVision.Postmaster.Application.Feedback.Commands.ReconcileDeliveryFeedback;
using TaxVision.Postmaster.Domain.ValueObjects;
using TaxVision.Postmaster.Infrastructure.Feedback;
using TaxVision.Postmaster.Infrastructure.Locking;
using Wolverine;

namespace TaxVision.Postmaster.Api.Jobs;

/// <summary>
/// Respaldo del webhook: cada <see cref="DeliveryFeedbackReconciliationOptions.IntervalMinutes"/> pregunta
/// al proveedor por los correos que siguen sin resultado. Una sola réplica por vuelta.
/// </summary>
public sealed class DeliveryFeedbackReconciliationJob(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    JobLeaderLock leaderLock,
    IOptions<DeliveryFeedbackReconciliationOptions> options,
    ILogger<DeliveryFeedbackReconciliationJob> logger
) : BackgroundService
{
    private const string LeaderLockKey = "postmaster:feedback-reconciliation:leader";

    /// <summary>Más largo que una corrida completa (50 consultas a ~1/s): no hace falta renovarlo.</summary>
    private static readonly TimeSpan LeaderLockTtl = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Invoca por bus: correr antes de que Wolverine arranque revienta.
        await lifetime.WaitForApplicationStartedAsync(stoppingToken);
        if (!await PullIsEnabledAsync())
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(5, options.Value.IntervalMinutes)));
        do
        {
            await RunOnceSafeAsync(stoppingToken);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task<bool> PullIsEnabledAsync()
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var capabilities = scope.ServiceProvider.GetRequiredService<IDeliveryFeedbackCapabilities>();
        if (capabilities.SupportsPull(FeedbackSource.Smtp2Go))
            return true;

        logger.LogWarning(
            "Delivery feedback pull is disabled: DeliveryFeedback:Smtp2Go:ApiKey is not set. The webhook still works."
        );
        return false;
    }

    private async Task RunOnceSafeAsync(CancellationToken ct)
    {
        try
        {
            await using var lease = await leaderLock.TryAcquireAsync(LeaderLockKey, LeaderLockTtl);
            if (lease is null)
                return;

            var settings = options.Value;
            var now = DateTime.UtcNow;
            await using var scope = scopeFactory.CreateAsyncScope();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            await bus.InvokeAsync<DeliveryFeedbackTally>(
                new ReconcileDeliveryFeedbackCommand(
                    now.AddHours(-settings.GiveUpAfterHours),
                    now.AddHours(-settings.ReconcileAfterHours),
                    settings.MaxRecipientsPerRun,
                    now
                ),
                ct
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Delivery feedback reconciliation failed.");
        }
    }
}
