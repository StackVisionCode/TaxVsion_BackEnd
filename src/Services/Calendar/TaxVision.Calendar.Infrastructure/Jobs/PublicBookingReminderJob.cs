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
/// Manda el recordatorio de la demo pública. La marca y el aviso van en la misma unidad: si dos réplicas toman
/// la misma reserva, la segunda choca con el <c>RowVersion</c> y no publica nada.
/// </summary>
internal sealed class PublicBookingReminderJob(
    IServiceScopeFactory scopeFactory,
    ILogger<PublicBookingReminderJob> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private const int BatchSize = 100;

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
                logger.LogError(ex, "PublicBookingReminderJob failed; retrying on the next tick.");
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
            targets = await bookings.ListDueRemindersAsync(nowUtc, BatchSize, ct);
        }
        if (targets.Count == 0)
            return new JobBatchSummary(0, 0, 0, 0);

        var summary = await TransactionalJobUnit.RunEachAsync<PublicBookingRef, CalendarDbContext>(
            scopeFactory,
            targets,
            async (target, unit, token) =>
            {
                var reminder = await PublicBookingMaintenance.RemindAsync(
                    target,
                    unit.Services.GetRequiredService<IPublicBookingRepository>(),
                    unit.Services.GetRequiredService<IBookingPageRepository>(),
                    unit.Services.GetRequiredService<PublicBookingNotices>(),
                    nowUtc,
                    token
                );
                if (reminder is null)
                    return false;
                // Se guarda primero: si el guardado choca, el aviso nunca sale.
                await unit.DbContext.SaveChangesAsync(token);
                await unit.PublishAsync(reminder);
                return true;
            },
            logger,
            ct
        );
        if (summary.Committed > 0)
            logger.LogInformation("PublicBookingReminderJob sent {Count} demo reminder(s).", summary.Committed);
        return summary;
    }
}
