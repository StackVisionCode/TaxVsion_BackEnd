using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Notification.Application.Abstractions;
using Wolverine;

namespace TaxVision.Notification.Api.Jobs;

/// <summary>
/// Servicio en segundo plano que detecta campañas programadas cuya hora ya llegó, las marca en ejecución
/// y publica el evento de inicio del fan-out. Fuera del request HTTP (patrón de background del repo).
/// </summary>
public sealed class CampaignSchedulerService(IServiceScopeFactory scopeFactory, ILogger<CampaignSchedulerService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private const int BatchSize = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessDueCampaignsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Campaign scheduler tick failed.");
            }
        }
    }

    private async Task ProcessDueCampaignsAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var campaigns = scope.ServiceProvider.GetRequiredService<IEmailCampaignRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var due = await campaigns.GetDueAsync(DateTime.UtcNow, BatchSize, ct);
        foreach (var campaign in due)
        {
            var running = campaign.MarkRunning();
            if (running.IsFailure)
                continue;

            // Se marca Running y se guarda ANTES de publicar (at-most-once) para no re-encolar el fan-out.
            await unitOfWork.SaveChangesAsync(ct);
            await bus.PublishAsync(
                new EmailCampaignStartedIntegrationEvent { CampaignId = campaign.Id, TenantId = campaign.TenantId }
            );
            logger.LogInformation("Campaign {CampaignId} started.", campaign.Id);
        }
    }
}
