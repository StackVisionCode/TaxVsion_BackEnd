using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Calendar.Application.PublicBookings;
using TaxVision.Calendar.Application.PublicBookings.Abstractions;
using TaxVision.Calendar.Infrastructure.Persistence;

namespace TaxVision.Calendar.Infrastructure.Jobs;

/// <summary>
/// Cierra las solicitudes de la demo pública cuya retención venció, para que el horario vuelva a ofrecerse
/// sin esperar a que otro visitante lo pida. Sin avisos: el visitante simplemente no confirmó a tiempo.
/// </summary>
internal sealed class PublicBookingHoldExpiryJob(
    IServiceScopeFactory scopeFactory,
    ILogger<PublicBookingHoldExpiryJob> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private const int BatchSize = 200;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await scopeFactory
            .CreateScope()
            .ServiceProvider.GetRequiredService<IHostApplicationLifetime>()
            .WaitForApplicationStartedAsync(stoppingToken);

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await RunAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "PublicBookingHoldExpiryJob failed; retrying on the next tick.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Una pasada: lee las referencias y procesa cada una en su unidad (BB-3).</summary>
    internal async Task<JobBatchSummary> RunAsync(DateTime nowUtc, CancellationToken ct)
    {
        IReadOnlyList<PublicBookingRef> targets;
        using (var scope = scopeFactory.CreateScope())
        {
            var bookings = scope.ServiceProvider.GetRequiredService<IPublicBookingRepository>();
            targets = await bookings.ListStaleHoldsAsync(nowUtc, BatchSize, ct);
        }
        if (targets.Count == 0)
            return new JobBatchSummary(0, 0, 0, 0);

        var summary = await TransactionalJobUnit.RunEachAsync<PublicBookingRef, CalendarDbContext>(
            scopeFactory,
            targets,
            async (target, unit, token) =>
            {
                var repository = unit.Services.GetRequiredService<IPublicBookingRepository>();
                return await PublicBookingMaintenance.ExpireHoldAsync(target, repository, nowUtc, token);
            },
            logger,
            ct
        );
        if (summary.Committed > 0)
            logger.LogInformation("PublicBookingHoldExpiryJob closed {Count} expired hold(s).", summary.Committed);
        return summary;
    }
}
