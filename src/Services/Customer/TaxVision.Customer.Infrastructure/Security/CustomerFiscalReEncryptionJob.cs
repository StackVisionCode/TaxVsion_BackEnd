using BuildingBlocks.Infrastructure.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Customer.Application.Customers.FiscalProfiles;

namespace TaxVision.Customer.Infrastructure.Security;

/// <summary>
/// Re-cifra al arrancar todo el ciphertext fiscal de Customer que no esté en la versión de clave actual
/// (B-9/X-9). Si no hay nada que re-cifrar —el caso normal— termina enseguida. Es idempotente: re-arrancar
/// tras completar no hace nada, porque las filas ya están en la versión actual.
/// </summary>
internal sealed class CustomerFiscalReEncryptionJob(
    IServiceScopeFactory scopeFactory,
    ILogger<CustomerFiscalReEncryptionJob> logger
) : BackgroundService
{
    private const int BatchSize = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await scopeFactory
            .CreateScope()
            .ServiceProvider.GetRequiredService<IHostApplicationLifetime>()
            .WaitForApplicationStartedAsync(stoppingToken);

        try
        {
            using var scope = scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<ICustomerFiscalCiphertextStore>();
            var report = await store.ReEncryptToCurrentAsync(BatchSize, stoppingToken);

            if (report.Total == 0)
                logger.LogInformation(
                    "Customer fiscal key version {Version}: no ciphertext to re-encrypt.",
                    report.CurrentKeyVersion
                );
            else
                logger.LogInformation(
                    "Customer fiscal re-encrypted {Total} row(s) to key version {Version}: {Breakdown}.",
                    report.Total,
                    report.CurrentKeyVersion,
                    string.Join(
                        ", ",
                        report
                            .Tables.Where(table => table.ReEncrypted > 0)
                            .Select(table => $"{table.Table}={table.ReEncrypted}")
                    )
                );
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Apagado: lo re-cifrado quedó commiteado; el resto se retoma en el próximo arranque.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Customer fiscal re-encryption failed; it will retry on the next start.");
        }
    }
}
