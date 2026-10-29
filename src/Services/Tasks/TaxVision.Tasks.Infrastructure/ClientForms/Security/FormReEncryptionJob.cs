using BuildingBlocks.Infrastructure.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Security;

/// <summary>
/// Re-cifra al arrancar todo el ciphertext de Client Forms que no esté en la versión de clave actual
/// (X-9.2). Si no hay nada que re-cifrar —el caso normal— es un solo conteo y termina. Es idempotente:
/// re-arrancar tras completar no hace nada, porque las filas ya están en la versión actual.
/// </summary>
internal sealed class FormReEncryptionJob(IServiceScopeFactory scopeFactory, ILogger<FormReEncryptionJob> logger)
    : BackgroundService
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
            var store = scope.ServiceProvider.GetRequiredService<IClientFormsCiphertextStore>();
            var report = await store.ReEncryptToCurrentAsync(BatchSize, stoppingToken);

            if (report.Total == 0)
                logger.LogInformation(
                    "Client forms key version {Version}: no ciphertext to re-encrypt.",
                    report.CurrentKeyVersion
                );
            else
                logger.LogInformation(
                    "Client forms re-encrypted {Total} row(s) to key version {Version}: {Breakdown}.",
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
            logger.LogError(ex, "Client forms re-encryption failed; it will retry on the next start.");
        }
    }
}
