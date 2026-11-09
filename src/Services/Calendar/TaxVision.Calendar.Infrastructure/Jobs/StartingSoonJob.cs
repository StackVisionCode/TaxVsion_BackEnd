using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Jobs;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Scheduling;
using TaxVision.Calendar.Infrastructure.Persistence;
using TaxVision.Calendar.Infrastructure.Persistence.Repositories;

namespace TaxVision.Calendar.Infrastructure.Jobs;

/// <summary>
/// Avisa de las citas que empiezan en un rato.
///
/// <para>
/// No lleva una marca en la cita: una serie no tiene fila por ocurrencia, así que un
/// <c>NotifiedAtUtc</c> apagaría el aviso de todas las siguientes. La marca es
/// <see cref="StartingSoonNotice"/>, una por ocurrencia, y su índice único decide quién avisa entre dos
/// réplicas o tras un reinicio.
/// </para>
///
/// <para>
/// Marca y aviso van en la misma unidad (BB-3): se guarda primero y se publica después, así que quien
/// pierde el índice no llega a publicar. Si una unidad falla, las demás siguen y la siguiente pasada la
/// retoma: toda la antelación es ventana, no solo un tick.
/// </para>
/// </summary>
internal sealed class StartingSoonJob(
    IServiceScopeFactory scopeFactory,
    IOptions<StartingSoonOptions> options,
    ILogger<StartingSoonJob> logger
) : BackgroundService
{
    private const string UniqueConstraintCode = "Persistence.UniqueConstraint";

    private TimeSpan Interval => options.Value.Interval;
    private TimeSpan Lead => options.Value.Lead;

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

    internal async Task NotifyAsync(CancellationToken ct)
    {
        var announcements = await CollectAsync(ct);
        if (announcements.Count == 0)
            return;

        var summary = await TransactionalJobUnit.RunEachAsync<Announcement, CalendarDbContext>(
            scopeFactory,
            announcements,
            PublishOnceAsync,
            logger,
            ct
        );

        if (summary.Committed > 0)
            logger.LogInformation("StartingSoonJob announced {Count} appointment(s) starting soon.", summary.Committed);
    }

    private static async Task<bool> PublishOnceAsync(
        Announcement announcement,
        JobUnitContext<CalendarDbContext> unit,
        CancellationToken ct
    )
    {
        var (targetId, notice) = announcement;
        var alreadyPublished = await unit
            .DbContext.StartingSoonNotices.IgnoreQueryFilters()
            .AnyAsync(
                n => n.TenantId == notice.TenantId && n.OccurrenceTargetId == targetId && n.StartUtc == notice.StartUtc,
                ct
            );
        if (alreadyPublished)
            return false;

        unit.DbContext.StartingSoonNotices.Add(
            StartingSoonNotice.Create(notice.TenantId, targetId, notice.StartUtc, DateTime.UtcNow)
        );

        try
        {
            await unit.DbContext.SaveChangesAsync(ct);
        }
        catch (ConflictException ex) when (ex.Code == UniqueConstraintCode)
        {
            return false;
        }

        await unit.PublishAsync(notice);
        return true;
    }

    /// <summary>Lectura sin escribir nada: lo que hay que anunciar en esta pasada.</summary>
    private async Task<List<Announcement>> CollectAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CalendarDbContext>();
        var correlationId = Guid.NewGuid().ToString("N");

        var from = DateTime.UtcNow;
        var to = from + Lead + Interval;

        var announcements = new List<Announcement>();
        foreach (var appointment in await Candidates(context, from, to).ToListAsync(ct))
            announcements.AddRange(AnnouncementsFor(appointment, from, to, correlationId));

        return announcements;
    }

    /// <summary>
    /// La consulta del job, expuesta para poder probarla contra la base: sin los <c>Include</c> las dos
    /// colecciones vuelven <b>vacias</b> y el job decide que no hay a quien avisar, sin un solo error.
    /// </summary>
    internal static IQueryable<Appointment> Candidates(CalendarDbContext context, DateTime fromUtc, DateTime toUtc) =>
        AppointmentRepository
            .ListStartingWithin(context, fromUtc, toUtc)
            .Include(a => a.Attendees)
            .Include(a => a.Exceptions);

    private static List<Announcement> AnnouncementsFor(
        Appointment appointment,
        DateTime from,
        DateTime to,
        string correlationId
    )
    {
        var announcements = new List<Announcement>();

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
            // El expander devuelve lo que solapa la ventana: una reunión que ya empezó no "empieza pronto".
            if (occurrence.StartUtc < from)
                continue;

            announcements.Add(
                new Announcement(
                    OccurrenceTargetId.For(appointment.Id, occurrence.OriginalStartUtc),
                    new AppointmentStartingSoonIntegrationEvent
                    {
                        TenantId = appointment.TenantId,
                        CorrelationId = correlationId,
                        AppointmentId = appointment.Id,
                        StartUtc = occurrence.StartUtc,
                        AttendeeUserIds = userIds,
                    }
                )
            );
        }

        return announcements;
    }

    private sealed record Announcement(Guid TargetId, AppointmentStartingSoonIntegrationEvent Notice);
}
