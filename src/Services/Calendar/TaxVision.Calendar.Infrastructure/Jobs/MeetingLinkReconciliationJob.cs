using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Infrastructure.Persistence;
using Wolverine;

namespace TaxVision.Calendar.Infrastructure.Jobs;

/// <summary>
/// Vuelve a pedir la sala de las citas virtuales que se quedaron sin ella.
///
/// <para>
/// Si Communication estuvo caído más de lo que aguanta el reintento de la cola, o si su handler falló,
/// la cita queda marcada como virtual y sin código corto: al usuario le aparece una casilla de «reunión
/// virtual» que no lleva a ningún sitio.
/// </para>
///
/// <para>
/// Pide la sala con su propio evento, no republicando el de la cita agendada: ése lleva los
/// destinatarios y volvería a mandarle la invitación a todo el mundo.
/// </para>
///
/// <para>
/// Cada corrección se registra con <b>WARN</b>: si aparece seguido, no es el job haciendo su trabajo,
/// es un handler de Communication fallando y hay que ir a mirarlo.
/// </para>
/// </summary>
internal sealed class MeetingLinkReconciliationJob(
    IServiceScopeFactory scopeFactory,
    ILogger<MeetingLinkReconciliationJob> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    /// <summary>Margen para no pisar el camino normal: una sala tarda segundos, no minutos.</summary>
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(5);

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
                await ReconcileAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "MeetingLinkReconciliationJob failed; retrying on the next tick.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ReconcileAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CalendarDbContext>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var correlation = scope.ServiceProvider.GetRequiredService<ICorrelationContext>();

        using var correlationScope = correlation.Push(Guid.NewGuid().ToString("N"));

        var cutoff = DateTime.UtcNow - Grace;
        var orphans = await context
            .Appointments.IgnoreQueryFilters()
            .Where(a =>
                a.IsVirtual && a.MeetingId == null && a.Status != AppointmentStatus.Cancelled && a.CreatedAtUtc < cutoff
            )
            .Take(BatchSize)
            .ToListAsync(ct);

        if (orphans.Count == 0)
            return;

        foreach (var appointment in orphans)
            await bus.PublishAsync(Rebuild(appointment, correlation.CorrelationId));

        logger.LogWarning(
            "MeetingLinkReconciliationJob re-requested {Count} meeting room(s) that never came back. "
                + "If this repeats, a Communication handler is failing.",
            orphans.Count
        );
    }

    private static AppointmentMeetingRoomRequestedIntegrationEvent Rebuild(
        Appointment appointment,
        string correlationId
    ) =>
        new()
        {
            TenantId = appointment.TenantId,
            CorrelationId = correlationId,
            AppointmentId = appointment.Id,
            Title = appointment.Title.Value,
            OrganizerUserId = appointment.OrganizerUserId,
            StartUtc = appointment.Timing.StartUtc,
        };
}
