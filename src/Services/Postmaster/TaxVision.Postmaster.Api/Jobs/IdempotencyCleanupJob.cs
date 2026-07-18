using Microsoft.EntityFrameworkCore;
using TaxVision.Postmaster.Infrastructure.Persistence;

namespace TaxVision.Postmaster.Api.Jobs;

/// <summary>Borra cada hora las reservas de <c>EmailIdempotency</c> ya expiradas.</summary>
public sealed class IdempotencyCleanupJob(IServiceScopeFactory scopeFactory, ILogger<IdempotencyCleanupJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

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

        var deleted = await dbContext
            .EmailIdempotencies.Where(e => e.ExpiresAtUtc < DateTime.UtcNow)
            .ExecuteDeleteAsync(ct);

        if (deleted > 0)
            logger.LogInformation("Idempotency cleanup removed {Count} expired reservation(s).", deleted);
    }
}
