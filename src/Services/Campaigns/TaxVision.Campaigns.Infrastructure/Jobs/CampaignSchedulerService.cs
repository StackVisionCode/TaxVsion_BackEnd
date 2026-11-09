using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Campaigns.Application.Runs;
using TaxVision.Campaigns.Application.Scheduling;
using TaxVision.Campaigns.Application.Scheduling.Abstractions;
using TaxVision.Campaigns.Application.Scheduling.Commands;
using Wolverine;

namespace TaxVision.Campaigns.Infrastructure.Jobs;

/// <summary>
/// Scheduler durable de campañas (SendMode Scheduled/Recurring). Cada tick reclama con <b>lease</b> los
/// schedules vencidos y dispara cada uno con <see cref="FireCampaignScheduleCommand"/>, que crea el run y
/// avanza el agendado en una sola transacción con outbox. El job no guarda ni publica por su cuenta. El
/// <b>solape</b> se evita porque el claim excluye schedules con <c>ActiveRunId</c>, que se libera al
/// consumir <c>run.completed</c>.
/// </summary>
public sealed class CampaignSchedulerService(IServiceProvider services, ILogger<CampaignSchedulerService> logger)
    : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    private const int LeaseSeconds = 120;
    private const int BatchSize = 20;
    private const string ConcurrencyConflictCode = "Persistence.ConcurrencyConflict";

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
        var options = scope.ServiceProvider.GetRequiredService<IOptions<CampaignSchedulerOptions>>().Value;

        if (options.HealStuckSchedules)
        {
            var healed = await schedules.ReleaseStuckAsync(DateTime.UtcNow, options.HealGrace, ct);
            if (healed.Released + healed.Paused > 0)
                logger.LogWarning(
                    "Healed stuck schedules: {Released} released, {Paused} paused (slot already passed).",
                    healed.Released,
                    healed.Paused
                );
        }

        var due = await schedules.ClaimDueAsync(DateTime.UtcNow, LeaseSeconds, BatchSize, ct);
        foreach (var claimed in due)
        {
            // Lease vencido mientras se disparaban los anteriores: lo toma el próximo tick.
            if (claimed.LeasedUntilUtc <= DateTime.UtcNow)
                continue;

            // Un error en un agendado no corta el lote.
            try
            {
                await bus.InvokeAsync<Result<CampaignRunResponse>>(
                    new FireCampaignScheduleCommand(
                        claimed.TenantId,
                        claimed.Id,
                        claimed.LeaseToken,
                        claimed.NextFireAtUtc
                    ),
                    ct
                );
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (CustomerAudienceUnavailableException)
            {
                // Transitorio: rollback; el lease vence y el próximo claim reintenta el slot.
                logger.LogWarning("Schedule {ScheduleId} waits for the client directory; will retry.", claimed.Id);
            }
            catch (ConflictException ex) when (ex.Code == ConcurrencyConflictCode)
            {
                // El agendado cambió mientras se disparaba (pausa, cancelación): rollback, nada salió.
                logger.LogInformation("Schedule {ScheduleId} changed while firing; skipped.", claimed.Id);
            }
            catch (Exception ex) when (ex is ConflictException or DbUpdateException)
            {
                // Otro proceso ya creó el run de este slot (clave primaria / índice único).
                logger.LogInformation(
                    "Schedule {ScheduleId} slot {Slot:o} already fired by another process.",
                    claimed.Id,
                    claimed.NextFireAtUtc
                );
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Schedule {ScheduleId} fire failed; the lease will expire and retry.", claimed.Id);
            }
        }
    }
}
