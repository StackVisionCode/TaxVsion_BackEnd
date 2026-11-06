using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Jobs;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Scheduling;
using TaxVision.Calendar.Infrastructure.Persistence;

namespace TaxVision.Calendar.Infrastructure.Jobs;

/// <summary>
/// Avisa de las citas que empiezan en un rato.
///
/// <para>
/// No lleva una marca en la fila: una serie no tiene fila por ocurrencia, así que un
/// <c>NotifiedAtUtc</c> en la cita apagaría el aviso de todas las siguientes. La ventana que se
/// consulta es del ancho exacto del tick, de modo que cada ocurrencia cae en una sola pasada.
/// </para>
///
/// <para>
/// El conjunto en memoria cubre el único hueco que deja: un reinicio dentro de la ventana la
/// recorrería otra vez. Se pierde al reiniciar, y está bien — repetir un aviso molesta, no avisar
/// deja a alguien llegando tarde.
/// </para>
///
/// <para>
/// Cada aviso sale por el outbox de su propia unidad (BB-3): si uno falla, los demás siguen.
/// </para>
/// </summary>
internal sealed class StartingSoonJob(IServiceScopeFactory scopeFactory, ILogger<StartingSoonJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Lead = TimeSpan.FromMinutes(15);

    private readonly HashSet<Guid> published = [];

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
                await NotifyAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "StartingSoonJob failed; retrying on the next tick.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task NotifyAsync(CancellationToken ct)
    {
        var announcements = await CollectAsync(ct);
        if (announcements.Count == 0)
            return;

        var summary = await TransactionalJobUnit.RunEachAsync<
            AppointmentStartingSoonIntegrationEvent,
            CalendarDbContext
        >(
            scopeFactory,
            announcements,
            async (announcement, unit, _) =>
            {
                await unit.PublishAsync(announcement);
                return true;
            },
            logger,
            ct
        );

        Forget();

        if (summary.Committed > 0)
            logger.LogInformation("StartingSoonJob announced {Count} appointment(s) starting soon.", summary.Committed);
    }

    /// <summary>Lectura sin escribir nada: lo que hay que anunciar en esta pasada.</summary>
    private async Task<List<AppointmentStartingSoonIntegrationEvent>> CollectAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CalendarDbContext>();
        var correlationId = Guid.NewGuid().ToString("N");

        var from = DateTime.UtcNow + Lead;
        var to = from + Interval;

        var announcements = new List<AppointmentStartingSoonIntegrationEvent>();
        foreach (var appointment in await Candidates(context).ToListAsync(ct))
            announcements.AddRange(AnnouncementsFor(appointment, from, to, correlationId));

        return announcements;
    }

    /// <summary>
    /// La consulta del job, expuesta para poder probarla contra la base: sin los <c>Include</c> las dos
    /// colecciones vuelven <b>vacias</b> y el job decide que no hay a quien avisar, sin un solo error.
    /// </summary>
    internal static IQueryable<Appointment> Candidates(CalendarDbContext context) =>
        context
            .Appointments.IgnoreQueryFilters()
            .Include(a => a.Attendees)
            .Include(a => a.Exceptions)
            .Where(a => a.Status != AppointmentStatus.Cancelled);

    private List<AppointmentStartingSoonIntegrationEvent> AnnouncementsFor(
        Appointment appointment,
        DateTime from,
        DateTime to,
        string correlationId
    )
    {
        var announcements = new List<AppointmentStartingSoonIntegrationEvent>();

        var userIds = new List<Guid>();
        foreach (var attendee in appointment.Attendees)
        {
            if (attendee.UserId is { } userId)
                userIds.Add(userId);
        }

        if (userIds.Count == 0)
            return announcements;

        var occurrences = OccurrenceExpander.Expand(appointment, from, to);
        if (occurrences.IsFailure)
            return announcements;

        foreach (var occurrence in occurrences.Value)
        {
            var targetId = OccurrenceTargetId.For(appointment.Id, occurrence.OriginalStartUtc);
            if (!published.Add(targetId))
                continue;

            announcements.Add(
                new AppointmentStartingSoonIntegrationEvent
                {
                    TenantId = appointment.TenantId,
                    CorrelationId = correlationId,
                    AppointmentId = appointment.Id,
                    StartUtc = occurrence.StartUtc,
                    AttendeeUserIds = userIds,
                }
            );
        }

        return announcements;
    }

    /// <summary>
    /// El id de la ocurrencia no lleva la hora dentro, así que no hay por dónde vaciar sólo lo viejo: se
    /// vacía entero al crecer. Lo peor que puede pasar es repetir un aviso de la ventana en curso.
    /// </summary>
    private void Forget()
    {
        if (published.Count > 10_000)
            published.Clear();
    }
}
