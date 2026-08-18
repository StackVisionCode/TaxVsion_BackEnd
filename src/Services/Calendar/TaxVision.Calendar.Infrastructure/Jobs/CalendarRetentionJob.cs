using BuildingBlocks.Infrastructure.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Scheduling;
using TaxVision.Calendar.Infrastructure.Persistence;

namespace TaxVision.Calendar.Infrastructure.Jobs;

/// <summary>
/// Borra lo que ya no le sirve a nadie.
///
/// <para>
/// <b>Una serie sin <c>UNTIL</c> ni <c>COUNT</c> no se borra nunca</b>, y no es un olvido: no tiene
/// última ocurrencia, así que no hay fecha desde la cual llamarla vieja. Purgarla por su fecha de
/// creación borraría la reunión semanal que el despacho tiene desde hace ocho años y sigue teniendo.
/// Las que sí terminan se borran cuando su última ocurrencia quedó atrás, y eso además alivia la
/// consulta de rango, que carga <b>todas</b> las series del tenant.
/// </para>
///
/// <para>
/// Se guarda por lotes chicos: una fila conflictiva tumbaría el borrado entero si fuera una sola
/// transacción, y a la siguiente pasada volvería a tumbarlo.
/// </para>
/// </summary>
internal sealed class CalendarRetentionJob(IServiceScopeFactory scopeFactory, ILogger<CalendarRetentionJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    /// <summary>
    /// Siete años, que es lo que el negocio guarda de un expediente fiscal: una cita vieja es parte
    /// del rastro de lo que se hizo con un cliente.
    /// </summary>
    private static readonly TimeSpan Retention = TimeSpan.FromDays(365 * 7);

    private const int ChunkSize = 50;

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
                await PurgeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "CalendarRetentionJob failed; retrying on the next tick.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PurgeAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CalendarDbContext>();

        var cutoff = DateTime.UtcNow - Retention;

        // Dos consultas y no una: toda serie es candidata, asi que un unico Take se llenaria de series
        // vivas y las puntuales vencidas no entrarian nunca en el lote. Ademas las puntuales salen por
        // indice y las series no — mezclarlas desperdicia el indice.
        //
        // IgnoreQueryFilters: fuera de un request no hay tenant en el scope y el filtro global
        // devolveria cero filas siempre, con el job pareciendo sano.
        var expiredOneOffs = await context
            .Appointments.IgnoreQueryFilters()
            .Where(a => a.Recurrence == null && a.Timing.EndUtc < cutoff)
            .Take(ChunkSize * 20)
            .ToListAsync(ct);

        var series = await context
            .Appointments.IgnoreQueryFilters()
            .Include(a => a.Exceptions)
            .Where(a => a.Recurrence != null)
            .ToListAsync(ct);

        var expired = new List<Appointment>(expiredOneOffs);
        foreach (var appointment in series)
        {
            if (IsExpired(appointment, cutoff))
                expired.Add(appointment);
        }

        if (expired.Count == 0)
            return;

        var removed = 0;
        for (var start = 0; start < expired.Count; start += ChunkSize)
        {
            var chunk = expired.GetRange(start, Math.Min(ChunkSize, expired.Count - start));
            context.Appointments.RemoveRange(chunk);

            try
            {
                await context.SaveChangesAsync(ct);
                removed += chunk.Count;
            }
            catch (DbUpdateException ex)
            {
                // Un lote perdido no puede llevarse los demás: se registra y se sigue con el siguiente.
                logger.LogWarning(ex, "CalendarRetentionJob could not remove a chunk of {Count}.", chunk.Count);
                foreach (var entry in chunk)
                    context.Entry(entry).State = EntityState.Unchanged;
            }
        }

        logger.LogInformation(
            "CalendarRetentionJob removed {Count} appointment(s) older than the retention window.",
            removed
        );
    }

    private static bool IsExpired(Appointment appointment, DateTime cutoff)
    {
        if (appointment.Recurrence is null)
            return appointment.Timing.EndUtc is { } endUtc && endUtc < cutoff;

        // Sin fin declarado no hay última ocurrencia y la serie se queda.
        if (!appointment.Recurrence.HasEnd)
            return false;

        // Se le pregunta al expander en vez de reimplementar el RRULE: si no queda ni una ocurrencia
        // desde el corte hasta dentro de un siglo, la serie terminó.
        var remaining = OccurrenceExpander.Expand(appointment, cutoff, cutoff.AddYears(100));
        return remaining.IsSuccess && remaining.Value.Count == 0;
    }
}
