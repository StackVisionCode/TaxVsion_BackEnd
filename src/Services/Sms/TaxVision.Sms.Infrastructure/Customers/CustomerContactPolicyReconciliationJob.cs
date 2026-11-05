using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Sms.Application.Customers;
using TaxVision.Sms.Application.Customers.Abstractions;

namespace TaxVision.Sms.Infrastructure.Customers;

/// <summary>
/// Llena y repara la proyección de la política de contacto desde Customer (todos los tenants). Los
/// eventos solo cubren lo ocurrido desde que el consumer existe; esto cubre lo anterior y lo perdido.
/// Idempotente; un cliente por scope y guardado, así un choque con el consumer descarta solo ese cliente.
/// </summary>
internal sealed class CustomerContactPolicyReconciliationJob(
    IServiceProvider serviceProvider,
    IOptions<SmsContactPolicyOptions> options,
    ILogger<CustomerContactPolicyReconciliationJob> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lifetime = serviceProvider.GetRequiredService<IHostApplicationLifetime>();
        await lifetime.WaitForApplicationStartedAsync(stoppingToken);

        var settings = options.Value;
        if (!settings.ReconciliationEnabled)
        {
            logger.LogInformation("CustomerContactPolicyReconciliationJob disabled by config; not running.");
            return;
        }

        var interval = TimeSpan.FromHours(Math.Max(1, settings.ReconciliationIntervalHours));
        var pageSize = Math.Max(1, settings.ReconciliationPageSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(pageSize, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "CustomerContactPolicyReconciliationJob iteration failed.");
            }

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

    private async Task RunOnceAsync(int pageSize, CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<ISmsCustomerPolicyClient>();

        var page = 1;
        var written = 0;
        while (!ct.IsCancellationRequested)
        {
            var snapshotAtUtc = DateTime.UtcNow;
            var result = await client.ListPageAsync(page, pageSize, ct);
            if (result is null)
            {
                logger.LogWarning(
                    "CustomerContactPolicyReconciliationJob aborted on page {Page} (Customer unreachable or unauthorized).",
                    page
                );
                return;
            }

            foreach (var contact in result.Items)
                written += await ReconcileAsync(contact, snapshotAtUtc, ct) ? 1 : 0;

            if (!result.HasMore)
                break;
            page++;
        }

        logger.LogInformation("CustomerContactPolicyReconciliationJob wrote {Count} contact policy row(s).", written);
    }

    private async Task<bool> ReconcileAsync(
        RemoteCustomerContactPolicy contact,
        DateTime snapshotAtUtc,
        CancellationToken ct
    )
    {
        using var scope = serviceProvider.CreateScope();
        var services = scope.ServiceProvider;
        try
        {
            var written = await CustomerContactPolicySnapshot.ApplyAsync(
                services.GetRequiredService<ICustomerContactPolicyRepository>(),
                contact,
                snapshotAtUtc,
                ct
            );
            await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(ct);
            return written;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "CustomerContactPolicyReconciliationJob skipped customer {CustomerId} of tenant {TenantId}.",
                contact.CustomerId,
                contact.TenantId
            );
            return false;
        }
    }
}
