using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Calendar.Application.Appointments;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Infrastructure.Persistence;
using TaxVision.Calendar.Infrastructure.Persistence.Repositories;
using Wolverine;

namespace TaxVision.Calendar.Infrastructure.Jobs;

/// <summary>
/// Pide a Reminder un aviso por cada ocurrencia del horizonte.
///
/// <para>
/// Reminder identifica su objetivo con un solo id y una serie tiene N ocurrencias, así que un
/// recordatorio por serie dispararía una vez y ya. Este job recorre las ocurrencias del horizonte
/// (<see cref="AppointmentReminderRequests.HorizonDays"/>) y pide una por una, con un id derivado de la
/// cita y del inicio original.
/// </para>
///
/// <para>
/// Republicar es gratis: Reminder deduplica por (destinatario, <c>RequestKey</c>). Por eso el job puede
/// correr a diario sin llevar cuenta de lo que ya pidió, y si el organizador cambió, el pedido crea el
/// aviso del nuevo. El armado vive en <see cref="AppointmentReminderRequests"/>.
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

        var horizon = now.AddDays(AppointmentReminderRequests.HorizonDays);
        var appointments = await Candidates(context, now, horizon).ToListAsync(ct);

        var requested = 0;
        foreach (var appointment in appointments)
            requested += await RequestForAsync(appointment, now, bus, correlation);

        if (requested > 0)
            logger.LogInformation("ReminderScheduleJob requested {Count} occurrence reminder(s).", requested);
    }

    /// <summary>
    /// La consulta del job, expuesta para poder probarla contra la base: el <c>Include</c> de las
    /// excepciones no es opcional — el expander las lee para saltar las ocurrencias canceladas y
    /// corregir la hora de las movidas. Sin el se piden avisos de reuniones que ya no existen.
    /// </summary>
    internal static IQueryable<Appointment> Candidates(CalendarDbContext context, DateTime fromUtc, DateTime toUtc) =>
        AppointmentRepository
            .ListStartingWithin(context, fromUtc, toUtc)
            .Include(a => a.Exceptions)
            .Where(a => a.ReminderLeadMinutes != null);

    private static async Task<int> RequestForAsync(
        Appointment appointment,
        DateTime now,
        IMessageBus bus,
        ICorrelationContext correlation
    )
    {
        var requests = AppointmentReminderRequests.For(appointment, now, correlation.CorrelationId);
        foreach (var request in requests)
            await bus.PublishAsync(request);

        return requests.Count;
    }
}
