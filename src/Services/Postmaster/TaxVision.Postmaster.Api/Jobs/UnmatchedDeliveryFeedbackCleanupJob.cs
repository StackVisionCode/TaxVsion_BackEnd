using Microsoft.EntityFrameworkCore;
using TaxVision.Postmaster.Infrastructure.Persistence;

namespace TaxVision.Postmaster.Api.Jobs;

/// <summary>Borra una vez al día los avisos sin dueño de más de 30 días: solo sirven para diagnóstico.</summary>
public sealed class UnmatchedDeliveryFeedbackCleanupJob(
    IServiceScopeFactory scopeFactory,
    ILogger<UnmatchedDeliveryFeedbackCleanupJob> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            await RunOnceAsync(stoppingToken);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PostmasterDbContext>();
        var cutoff = DateTime.UtcNow - Retention;

        var deleted = await dbContext
            .UnmatchedDeliveryFeedback.Where(f => f.ReceivedAtUtc < cutoff)
            .ExecuteDeleteAsync(ct);

        if (deleted > 0)
            logger.LogInformation("Removed {Count} unmatched delivery feedback row(s) older than 30 days.", deleted);
    }
}
