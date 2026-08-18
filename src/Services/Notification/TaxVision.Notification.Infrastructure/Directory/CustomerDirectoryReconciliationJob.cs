using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Directory.Abstractions;
using TaxVision.Notification.Domain.Directory;

namespace TaxVision.Notification.Infrastructure.Directory;

/// <summary>
/// Repasa la lista completa de clientes —todos los tenants, con token de PlatformTenant— y hace
/// upsert de cada dirección.
///
/// <para>
/// <b>Por qué hacía falta.</b> El directorio se llenaba sólo por eventos, así que cubría lo ocurrido
/// desde que el consumer existe: los clientes anteriores nunca entraron y un evento perdido dejaba un
/// hueco permanente. El fallo era silencioso —el consumer del correo no encontraba la dirección,
/// salía sin error y el cliente no recibía nada—, y por eso pasó desapercibido hasta que se midió la
/// tabla y estaba vacía.
/// </para>
///
/// <para>
/// Idempotente: usa los mismos mutadores del dominio que el consumer de eventos, así que correrlo N
/// veces converge. Persiste por página para no dejar crecer el change-tracker.
/// </para>
/// </summary>
internal sealed class CustomerDirectoryReconciliationJob(
    IServiceProvider serviceProvider,
    ILogger<CustomerDirectoryReconciliationJob> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lifetime = serviceProvider.GetRequiredService<IHostApplicationLifetime>();
        await lifetime.WaitForApplicationStartedAsync(stoppingToken);

        var options = serviceProvider.GetRequiredService<IOptions<NotificationCustomerClientOptions>>().Value;
        if (!options.ReconciliationEnabled)
        {
            logger.LogInformation("CustomerDirectoryReconciliationJob disabled by config; not running.");
            return;
        }

        var interval = TimeSpan.FromHours(Math.Max(1, options.ReconciliationIntervalHours));
        var pageSize = Math.Max(1, options.ReconciliationPageSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceSafeAsync(pageSize, stoppingToken);

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    private async Task RunOnceSafeAsync(int pageSize, CancellationToken ct)
    {
        try
        {
            await RunOnceAsync(pageSize, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CustomerDirectoryReconciliationJob iteration failed.");
        }
    }

    private async Task RunOnceAsync(int pageSize, CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<INotificationCustomerClient>();
        var repository = scope.ServiceProvider.GetRequiredService<ICustomerEmailDirectoryRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var page = 1;
        var written = 0;

        while (!ct.IsCancellationRequested)
        {
            var result = await client.ListAllForReconciliationAsync(page, pageSize, ct);
            if (result is null)
            {
                logger.LogWarning(
                    "CustomerDirectoryReconciliationJob aborted on page {Page} (Customer unreachable or unauthorized).",
                    page
                );
                return;
            }

            foreach (var contact in result.Items)
                written += await UpsertAsync(repository, contact, ct) ? 1 : 0;

            await unitOfWork.SaveChangesAsync(ct);

            if (!result.HasMore)
                break;

            page++;
        }

        logger.LogInformation("CustomerDirectoryReconciliationJob wrote {Count} customer address(es).", written);
    }

    private static async Task<bool> UpsertAsync(
        ICustomerEmailDirectoryRepository repository,
        RemoteCustomerContact contact,
        CancellationToken ct
    )
    {
        var normalized = CustomerEmailDirectoryEntry.Normalize(contact.PrimaryEmail);
        var existing = await repository.GetByCustomerIdAsync(contact.TenantId, contact.CustomerId, ct);

        if (existing is not null)
        {
            existing.Reconcile(normalized, contact.DisplayName, contact.IsActive);
            return true;
        }

        // Sin dirección no se crea la fila: una entrada vacía haría creer que el cliente es
        // alcanzable y el envío fallaría en silencio en vez de saltarse el aviso con un motivo claro.
        if (normalized.Length == 0)
            return false;

        await repository.AddAsync(
            CustomerEmailDirectoryEntry.Create(contact.TenantId, contact.CustomerId, normalized, contact.DisplayName),
            ct
        );
        return true;
    }
}
