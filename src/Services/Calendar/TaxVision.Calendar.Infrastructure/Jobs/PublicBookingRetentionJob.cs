using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.PublicBookings;
using TaxVision.Calendar.Application.PublicBookings.Abstractions;
using TaxVision.Calendar.Infrastructure.Persistence;

namespace TaxVision.Calendar.Infrastructure.Jobs;

/// <summary>
/// Retención de la demo pública (D-04): borra las solicitudes que nunca se confirmaron y anonimiza las demos
/// cerradas hace más de los meses configurados (también al visitante en la cita del anfitrión).
/// </summary>
internal sealed class PublicBookingRetentionJob(
    IServiceScopeFactory scopeFactory,
    IOptions<PublicBookingOptions> options,
    ILogger<PublicBookingRetentionJob> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private const int BatchSize = 500;

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
                logger.LogError(ex, "PublicBookingRetentionJob failed; retrying on the next tick.");
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
            targets = await bookings.ListPurgeCandidatesAsync(
                nowUtc.AddHours(-options.Value.UnverifiedRetentionHours),
                nowUtc.AddMonths(-options.Value.ClosedRetentionMonths),
                BatchSize,
                ct
            );
        }
        if (targets.Count == 0)
            return new JobBatchSummary(0, 0, 0, 0);

        var summary = await TransactionalJobUnit.RunEachAsync<PublicBookingRef, CalendarDbContext>(
            scopeFactory,
            targets,
            async (target, unit, token) =>
            {
                return await PublicBookingMaintenance.PurgeAsync(
                    target,
                    unit.Services.GetRequiredService<IPublicBookingRepository>(),
                    unit.Services.GetRequiredService<IAppointmentRepository>(),
                    options.Value,
                    nowUtc,
                    token
                );
            },
            logger,
            ct
        );
        if (summary.Committed > 0)
            logger.LogInformation(
                "PublicBookingRetentionJob purged or anonymized {Count} public booking(s).",
                summary.Committed
            );
        return summary;
    }
}
