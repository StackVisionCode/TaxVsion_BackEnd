using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Scheduling;
using TaxVision.Calendar.Infrastructure.Persistence;
using Wolverine;

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
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CalendarDbContext>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var correlation = scope.ServiceProvider.GetRequiredService<ICorrelationContext>();

        using var correlationScope = correlation.Push(Guid.NewGuid().ToString("N"));

        var from = DateTime.UtcNow + Lead;
        var to = from + Interval;

        var appointments = await Candidates(context).ToListAsync(ct);

        var sent = 0;
        foreach (var appointment in appointments)
            sent += await NotifyForAsync(appointment, from, to, bus, correlation);

        Forget();

        if (sent > 0)
            logger.LogInformation("StartingSoonJob announced {Count} appointment(s) starting soon.", sent);
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

    private async Task<int> NotifyForAsync(
        Appointment appointment,
        DateTime from,
        DateTime to,
        IMessageBus bus,
        ICorrelationContext correlation
    )
    {
        var userIds = new List<Guid>();
        foreach (var attendee in appointment.Attendees)
        {
            if (attendee.UserId is { } userId)
                userIds.Add(userId);
        }

        if (userIds.Count == 0)
            return 0;

        var occurrences = OccurrenceExpander.Expand(appointment, from, to);
        if (occurrences.IsFailure)
            return 0;

        var sent = 0;
        foreach (var occurrence in occurrences.Value)
        {
            var targetId = OccurrenceTargetId.For(appointment.Id, occurrence.OriginalStartUtc);
            if (!published.Add(targetId))
                continue;

            await bus.PublishAsync(
                new AppointmentStartingSoonIntegrationEvent
                {
                    TenantId = appointment.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    AppointmentId = appointment.Id,
                    StartUtc = occurrence.StartUtc,
                    AttendeeUserIds = userIds,
                }
            );

            sent++;
        }

        return sent;
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
