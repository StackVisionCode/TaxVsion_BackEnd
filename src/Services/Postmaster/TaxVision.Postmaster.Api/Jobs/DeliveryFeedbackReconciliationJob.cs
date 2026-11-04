using BuildingBlocks.Infrastructure.Hosting;
using Microsoft.Extensions.Options;
using TaxVision.Postmaster.Application.Feedback;
using TaxVision.Postmaster.Application.Feedback.Commands.ReconcileDeliveryFeedback;
using TaxVision.Postmaster.Application.Feedback.Commands.ReconcileEngagementFeedback;
using TaxVision.Postmaster.Domain.ValueObjects;
using TaxVision.Postmaster.Infrastructure.Feedback;
using TaxVision.Postmaster.Infrastructure.Locking;
using Wolverine;

namespace TaxVision.Postmaster.Api.Jobs;

/// <summary>
/// Respaldo del webhook: cada <see cref="DeliveryFeedbackReconciliationOptions.IntervalMinutes"/> pregunta
/// al proveedor por los correos que siguen sin resultado y, después, por las aperturas y clics recientes.
/// Una sola réplica por vuelta.
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

    /// <summary>Más largo que una corrida completa (50 consultas a ~1/s + unas pocas páginas de aperturas).</summary>
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

    public static ReconcileDeliveryFeedbackCommand BuildDeliveryCommand(
        DeliveryFeedbackReconciliationOptions settings,
        DateTime now
    ) =>
        new(
            now.AddHours(-settings.GiveUpAfterHours),
            now.AddHours(-settings.ReconcileAfterHours),
            settings.MaxRecipientsPerRun,
            now
        );

    /// <summary>Null = apagado: las aperturas y clics llegan solo por el webhook.</summary>
    public static ReconcileEngagementFeedbackCommand? BuildEngagementCommand(
        DeliveryFeedbackReconciliationOptions settings,
        DateTime now
    ) =>
        settings.EngagementEnabled
            ? new ReconcileEngagementFeedbackCommand(
                now.AddHours(-settings.EngagementLookbackHours),
                now,
                settings.MaxEngagementEventsPerRun,
                now
            )
            : null;

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
            await bus.InvokeAsync<DeliveryFeedbackTally>(BuildDeliveryCommand(settings, now), ct);

            // Después de las entregas: si esto falla, los resultados de entrega ya quedaron aplicados.
            if (BuildEngagementCommand(settings, now) is { } engagement)
                await bus.InvokeAsync<DeliveryFeedbackTally>(engagement, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Delivery feedback reconciliation failed.");
        }
    }
}
