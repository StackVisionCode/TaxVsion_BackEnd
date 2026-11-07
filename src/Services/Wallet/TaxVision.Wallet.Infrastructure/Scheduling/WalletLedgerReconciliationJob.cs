using System.Diagnostics.Metrics;
using BuildingBlocks.Infrastructure.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Wallet.Application.Wallet.Abstractions;

namespace TaxVision.Wallet.Infrastructure.Scheduling;

/// <summary>
/// WAL-8: compara cada <see cref="WalletLedgerReconciliationOptions.Interval"/> el saldo con la suma del ledger.
/// Solo avisa, con log y métrica: cada corrección pasa por WAL-5 con decisión del dueño.
/// </summary>
public sealed class WalletLedgerReconciliationJob(
    IServiceScopeFactory scopeFactory,
    IDistributedLockFactory lockFactory,
    IHostApplicationLifetime lifetime,
    IOptions<WalletLedgerReconciliationOptions> options,
    ILogger<WalletLedgerReconciliationJob> logger
) : BackgroundService
{
    private const string LockKey = "wallet-ledger-reconciliation";

    // Meter con el nombre del servicio: OpenTelemetryRegistration ya lo exporta (wallet_ledger_mismatch_total).
    private static readonly Meter Meter = new("wallet-service");
    private static readonly Counter<long> MismatchCounter = Meter.CreateCounter<long>(
        "wallet.ledger.mismatch",
        description: "Tenants whose balance does not match the ledger, or that cannot be reconciled."
    );

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("Wallet ledger reconciliation job is disabled by configuration.");
            return;
        }

        await lifetime.WaitForApplicationStartedAsync(stoppingToken);

        using var timer = new PeriodicTimer(settings.Interval);
        do
        {
            try
            {
                await using var handle = await lockFactory.TryAcquireAsync(LockKey, settings.LockTtl, stoppingToken);
                if (handle is null)
                    continue;

                using var scope = scopeFactory.CreateScope();
                await ReportAsync(scope.ServiceProvider.GetRequiredService<IWalletRepository>(), logger, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Wallet ledger reconciliation iteration failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Una pasada: un aviso por tenant. Devuelve cuántos reportó. Público para probarlo con fakes.</summary>
    public static async Task<int> ReportAsync(IWalletRepository wallets, ILogger logger, CancellationToken ct)
    {
        var mismatches = await wallets.ListBalanceMismatchesAsync(ct);
        foreach (var mismatch in mismatches)
        {
            MismatchCounter.Add(1, new KeyValuePair<string, object?>("reconcilable", mismatch.IsReconcilable));
            if (mismatch.IsReconcilable)
                logger.LogError(
                    "Wallet of tenant {TenantId} does not match its ledger: posted {PostedMicros} vs {LedgerPostedMicros}, held {HeldMicros} vs {LedgerHeldMicros}.",
                    mismatch.TenantId,
                    mismatch.PostedMicros,
                    mismatch.LedgerPostedMicros,
                    mismatch.HeldMicros,
                    mismatch.LedgerHeldMicros
                );
            else
                logger.LogWarning(
                    "Tenant {TenantId} has {WalletCount} wallets and cannot be reconciled: the ledger has no currency.",
                    mismatch.TenantId,
                    mismatch.WalletCount
                );
        }

        return mismatches.Count;
    }
}
