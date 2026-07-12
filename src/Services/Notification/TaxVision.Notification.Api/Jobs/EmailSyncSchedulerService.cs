using BuildingBlocks.Messaging.EmailIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using Wolverine;

namespace TaxVision.Notification.Api.Jobs;

/// <summary>
/// Servicio en segundo plano que sincroniza periódicamente (incremental) las cuentas activas cuya
/// última sincronización superó el umbral. Encola el trabajo por evento; no sincroniza en el propio tick.
/// </summary>
public sealed class EmailSyncSchedulerService(
    IServiceScopeFactory scopeFactory,
    ILogger<EmailSyncSchedulerService> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SyncEvery = TimeSpan.FromMinutes(15);
    private const int BatchSize = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await EnqueueDueAccountsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Email sync scheduler tick failed.");
            }
        }
    }

    private async Task EnqueueDueAccountsAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IEmailAccountRepository>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var due = await accounts.GetDueForSyncAsync(DateTime.UtcNow - SyncEvery, BatchSize, ct);
        foreach (var account in due)
        {
            await bus.PublishAsync(
                new EmailIncrementalSyncRequestedIntegrationEvent
                {
                    AccountId = account.Id,
                    TenantId = account.TenantId,
                }
            );
        }
    }
}
