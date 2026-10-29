using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.Infrastructure.Persistence;
using Wolverine.EntityFrameworkCore;

namespace TaxVision.Tasks.Infrastructure.ClientForms;

/// <summary>
/// Publica <c>task.form_request_reminder_due.v1</c> por cada request abierta a la que le toca recordatorio.
///
/// <para>
/// El filtro grueso (ventana y tope) lo hace SQL; el cronograma fino (etapas previas y vencida cada N días)
/// lo decide <see cref="ReminderSchedule"/> en memoria. Cada envío se guarda en su propia transacción: con
/// varias instancias, el <c>RowVersion</c> hace que la segunda reciba 409 y salte esa request sin tocar el resto.
/// <b>Guardar antes de publicar</b>: el aviso va por el outbox de EF, así que solo sale si el contador se grabó.
/// </para>
/// </summary>
internal sealed class FormRequestReminderJob(
    IServiceScopeFactory scopeFactory,
    IOptions<ClientFormsOptions> options,
    ILogger<FormRequestReminderJob> logger
) : BackgroundService
{
    private const int BatchSize = 200;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wolverine aún no está listo cuando arrancan los hosted services: sin esta espera la primera
        // corrida lanza WolverineHasNotStartedException y se pierde entera.
        await scopeFactory
            .CreateScope()
            .ServiceProvider.GetRequiredService<IHostApplicationLifetime>()
            .WaitForApplicationStartedAsync(stoppingToken);

        var reminders = options.Value.Reminders;
        if (!reminders.Enabled)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(reminders.JobIntervalMinutes));
        do
        {
            try
            {
                await SweepAsync(reminders, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "FormRequestReminderJob failed; retrying on the next tick.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SweepAsync(ClientFormsReminderOptions reminders, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var maxDaysBefore = reminders.DaysBeforeDue.Length == 0 ? 0 : reminders.DaysBeforeDue.Max();
        // Candidatas: cualquier abierta cuya fecha límite ya entró en la ventana previa (o pasó).
        var threshold = now.AddDays(maxDaysBefore);

        List<Guid> candidateIds;
        using (var scope = scopeFactory.CreateScope())
        {
            var requests = scope.ServiceProvider.GetRequiredService<IFormRequestRepository>();
            var candidates = await requests.ListReminderCandidatesAsync(
                threshold,
                reminders.MaxReminders,
                BatchSize,
                ct
            );
            candidateIds = candidates.Select(item => item.Id).ToList();
        }

        var sent = 0;
        foreach (var id in candidateIds)
        {
            if (ct.IsCancellationRequested)
                break;
            if (await TrySendAsync(id, reminders, now, ct))
                sent++;
        }

        if (sent > 0)
            logger.LogInformation("FormRequestReminderJob sent {Sent} reminder(s).", sent);
    }

    // Una request por transacción: así una colisión de RowVersion solo salta esta y no arrastra al lote.
    private async Task<bool> TrySendAsync(
        Guid id,
        ClientFormsReminderOptions reminders,
        DateTime now,
        CancellationToken ct
    )
    {
        using var scope = scopeFactory.CreateScope();
        var requests = scope.ServiceProvider.GetRequiredService<IFormRequestRepository>();
        var outbox = scope.ServiceProvider.GetRequiredService<IDbContextOutbox<TasksDbContext>>();
        var correlation = scope.ServiceProvider.GetRequiredService<ICorrelationContext>();

        var found = await requests.GetForReminderAsync(id, ct);
        if (found is null)
            return false;

        var decision = ReminderSchedule.Evaluate(
            found.Status,
            found.DueAtUtc,
            found.LastReminderAtUtc,
            found.ReminderCount,
            now,
            reminders
        );
        if (!decision.ShouldSend)
            return false;

        var recorded = found.RecordReminder(reminders.MaxReminders, now);
        if (recorded.IsFailure)
            return false;

        await outbox.PublishAsync(
            FormRequestIntegrationEventFactory.ReminderDue(
                found,
                decision.IsOverdue,
                isManual: false,
                sentByUserId: null,
                correlation.CorrelationId
            )
        );
        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync(ct);
        }
        catch (ConflictException ex) when (ex.Code == "Persistence.ConcurrencyConflict")
        {
            // Otra instancia la recordó primero: el contador no subió aquí y el aviso no salió.
            logger.LogDebug("FormRequestReminderJob skipped request {RequestId} modified concurrently.", id);
            return false;
        }

        return true;
    }
}
