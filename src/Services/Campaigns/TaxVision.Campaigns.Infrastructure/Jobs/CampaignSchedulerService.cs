using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Campaigns.Application.Runs;
using TaxVision.Campaigns.Application.Runs.Commands;
using TaxVision.Campaigns.Application.Scheduling.Abstractions;
using Wolverine;

namespace TaxVision.Campaigns.Infrastructure.Jobs;

/// <summary>
/// Scheduler durable de campañas (SendMode Scheduled/Recurring). Cada tick reclama con <b>lease</b> los
/// schedules vencidos (no poll-sin-lease, anti-patrón legado), dispara un <c>CampaignRun</c> nuevo por
/// cada uno vía <see cref="StartCampaignRunFromAudienceCommand"/> (audiencia = ContactLists del schedule)
/// y avanza el schedule (OneTime→Completed, Recurring→próximo slot con coalescing de misfires). El
/// <b>solape</b> se evita porque el claim excluye schedules con <c>ActiveRunId</c>, que se libera al
/// consumir <c>run.completed</c>. SIN dinero (el cobro previo lo hará el PEP externo, diferido).
/// </summary>
public sealed class CampaignSchedulerService(IServiceProvider services, ILogger<CampaignSchedulerService> logger)
    : BackgroundService
{
    private static readonly Guid SchedulerActor = new("5c4ed011-0000-0000-0000-0000005c4ed0"); // "scheduler" system actor
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    private const int LeaseSeconds = 120;
    private const int BatchSize = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Pequeño arranque diferido para no competir con la migración/warmup del proceso.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Campaign scheduler tick failed; will retry next interval.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var schedules = scope.ServiceProvider.GetRequiredService<ICampaignScheduleRepository>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var now = DateTime.UtcNow;
        var due = await schedules.ClaimDueAsync(now, LeaseSeconds, BatchSize, ct);
        if (due.Count == 0)
            return;

        foreach (var schedule in due)
        {
            var fireTime = DateTime.UtcNow;
            var result = await bus.InvokeAsync<Result<CampaignRunResponse>>(
                new StartCampaignRunFromAudienceCommand(
                    schedule.TenantId,
                    schedule.CampaignId,
                    SchedulerActor,
                    schedule.ContactListIds(),
                    [],
                    TriggerKind: "Scheduled",
                    IncludeCustomers: schedule.IncludeCustomers
                ),
                ct
            );

            if (result.IsFailure)
            {
                // No pudo disparar (p.ej. sin audiencia resoluble). Avanza igual para no reintentar en bucle;
                // el lease expira solo si no guardamos, así que registramos y seguimos.
                logger.LogWarning(
                    "Schedule {ScheduleId} (campaign {CampaignId}) fire failed: {Error}. Advancing.",
                    schedule.Id,
                    schedule.CampaignId,
                    result.Error.Code
                );
                schedule.MarkFired(Guid.Empty, fireTime);
                await unitOfWork.SaveChangesAsync(ct);
                continue;
            }

            schedule.MarkFired(result.Value.Id, fireTime);
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation(
                "Fired schedule {ScheduleId} (campaign {CampaignId}) → run {RunId}; next={Next:o}.",
                schedule.Id,
                schedule.CampaignId,
                result.Value.Id,
                schedule.NextFireAtUtc
            );
        }
    }
}
