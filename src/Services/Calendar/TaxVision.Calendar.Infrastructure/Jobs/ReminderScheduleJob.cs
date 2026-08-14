using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Messaging.ReminderIntegrationEvents;
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
/// Pide a Reminder un aviso por cada ocurrencia del horizonte.
///
/// <para>
/// Reminder identifica su objetivo con un solo id y una serie tiene N ocurrencias, así que un
/// recordatorio por serie dispararía una vez y ya. Este job recorre las ocurrencias de los próximos
/// 60 días y pide una por una, con un id derivado de la cita y del inicio original.
/// </para>
///
/// <para>
/// Republicar es gratis: Reminder deduplica por <c>RequestKey</c>. Por eso el job puede correr a
/// diario sin llevar cuenta de lo que ya pidió.
/// </para>
///
/// <para>
/// La alternativa —enseñarle RRULE a Reminder— duplicaría el motor de recurrencia en el servicio que
/// justamente no conoce a nadie. La complejidad se queda del lado que ya tiene el motor.
/// </para>
/// </summary>
internal sealed class ReminderScheduleJob(IServiceScopeFactory scopeFactory, ILogger<ReminderScheduleJob> logger)
    : BackgroundService
{
    private const int HorizonDays = 60;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(12);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // La pasada de arranque publica, y Wolverine todavía no está listo cuando el host levanta los
        // hosted services: sin la espera lanza y la primera corrida se pierde entera.
        await scopeFactory
            .CreateScope()
            .ServiceProvider.GetRequiredService<IHostApplicationLifetime>()
            .WaitForApplicationStartedAsync(stoppingToken);

        using var timer = new PeriodicTimer(Interval);

        do
        {
            try
            {
                await RequestAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ReminderScheduleJob failed; retrying on the next tick.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RequestAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CalendarDbContext>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var correlation = scope.ServiceProvider.GetRequiredService<ICorrelationContext>();

        // El job es el origen de la traza: un id por pasada, para seguir junto todo lo que publique.
        using var correlationScope = correlation.Push(Guid.NewGuid().ToString("N"));

        var now = DateTime.UtcNow;
        var horizon = now.AddDays(HorizonDays);

        var appointments = await Candidates(context).ToListAsync(ct);

        var requested = 0;
        foreach (var appointment in appointments)
            requested += await RequestForAsync(appointment, now, horizon, bus, correlation);

        if (requested > 0)
            logger.LogInformation("ReminderScheduleJob requested {Count} occurrence reminder(s).", requested);
    }

    /// <summary>
    /// La consulta del job, expuesta para poder probarla contra la base: el <c>Include</c> de las
    /// excepciones no es opcional — el expander las lee para saltar las ocurrencias canceladas y
    /// corregir la hora de las movidas. Sin el se piden avisos de reuniones que ya no existen.
    /// </summary>
    internal static IQueryable<Appointment> Candidates(CalendarDbContext context) =>
        context
            .Appointments.IgnoreQueryFilters()
            .Include(a => a.Exceptions)
            .Where(a => a.ReminderLeadMinutes != null && a.Status != AppointmentStatus.Cancelled);

    private static async Task<int> RequestForAsync(
        Appointment appointment,
        DateTime now,
        DateTime horizon,
        IMessageBus bus,
        ICorrelationContext correlation
    )
    {
        var occurrences = OccurrenceExpander.Expand(appointment, now, horizon);
        if (occurrences.IsFailure)
            return 0;

        foreach (var occurrence in occurrences.Value)
        {
            var targetId = OccurrenceTargetId.For(appointment.Id, occurrence.OriginalStartUtc);

            await bus.PublishAsync(
                new ReminderRequestedIntegrationEvent
                {
                    TenantId = appointment.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    UserId = appointment.OrganizerUserId,
                    Category = "Calendar",
                    TargetId = targetId,
                    Title = occurrence.Title,
                    TimeZoneId = appointment.Timing.TimeZone.Id,
                    AnchorAtUtc = occurrence.StartUtc,
                    LeadMinutes = appointment.ReminderLeadMinutes,
                    RequestKey = $"calendar:{targetId:N}",
                }
            );
        }

        return occurrences.Value.Count;
    }
}
