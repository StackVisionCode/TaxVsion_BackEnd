using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.Sync;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Infrastructure.Observability;
using Wolverine;

namespace TaxVision.Connectors.Infrastructure.Jobs;

/// <summary>
/// Safety net de Gmail/Graph (detrás del push, Fase 7) y único mecanismo de sync de IMAP (que no
/// tiene push — ver SetupWatchHandler). Cada <see cref="ReconciliationOptions.IntervalMinutes"/>
/// escanea TODAS las cuentas Active de todos los tenants y re-invoca ReconcileAccountHandler por
/// cuenta — un solo job compartido, no un loop por cuenta (ese patrón, el de
/// ReactiveEmailReceivingService del backend legacy, no escala a muchos tenants). Mismo esqueleto
/// que WatchRenewalJob/ProactiveTokenRefreshJob: scope propio por cuenta, correlation fresca por
/// iteración (este job es el consumer boundary — no hay HTTP request/CorrelationIdMiddleware detrás
/// de un BackgroundService). Ver README §37.8 para el detalle completo, incluida la métrica a
/// vigilar en producción.
/// </summary>
public sealed class ReconciliationJob(
    IServiceProvider serviceProvider,
    IOptions<ReconciliationOptions> options,
    ILogger<ReconciliationJob> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Reemplaza el delay fijo anterior: RunOnceAsync invoca por bus (bus.InvokeAsync), y
        // correr antes de que Wolverine termine de arrancar revienta con
        // WolverineHasNotStartedException.
        var lifetime = serviceProvider.GetRequiredService<IHostApplicationLifetime>();
        await lifetime.WaitForApplicationStartedAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceSafeAsync(stoppingToken);
            try
            {
                var interval = TimeSpan.FromMinutes(Math.Max(1, options.Value.IntervalMinutes));
                await Task.Delay(interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunOnceSafeAsync(CancellationToken ct)
    {
        try
        {
            await RunOnceAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ReconciliationJob iteration failed.");
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var accountRepository = scope.ServiceProvider.GetRequiredService<ITenantEmailAccountRepository>();

        var accounts = await accountRepository.ListActiveAsync(ct);
        if (accounts.Count == 0)
            return;

        var scanned = 0;
        var accountsWithRecoveredMessages = 0;

        foreach (var account in accounts)
        {
            // Jitter — evita que las N cuentas activas le peguen a Gmail/Graph/IMAP en el mismo
            // instante en cada tick del job (thundering herd). No reemplaza al rate
            // limiter/circuit breaker de cada client, es cortesía adicional a nivel de esta corrida.
            if (scanned > 0)
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(50, 400)), ct);

            using var accountScope = serviceProvider.CreateScope();
            var bus = accountScope.ServiceProvider.GetRequiredService<IMessageBus>();
            var correlation = accountScope.ServiceProvider.GetRequiredService<ICorrelationContext>();
            var providerTag = account.ProviderCode.ToString();

            using (correlation.Push(Guid.NewGuid().ToString("N")))
            {
                Result<ReconciliationOutcome> result;
                try
                {
                    result = await bus.InvokeAsync<Result<ReconciliationOutcome>>(
                        new ReconcileAccountCommand(account.Id),
                        ct
                    );
                }
                catch (Exception ex)
                {
                    ConnectorsMetrics.ReconciliationErrors.Add(
                        1,
                        new KeyValuePair<string, object?>("provider", providerTag)
                    );
                    logger.LogError(
                        ex,
                        "ReconciliationJob threw while reconciling account {AccountId} ({Provider}).",
                        account.Id,
                        providerTag
                    );
                    continue;
                }

                scanned++;
                ConnectorsMetrics.ReconciliationAccountsScanned.Add(
                    1,
                    new KeyValuePair<string, object?>("provider", providerTag)
                );

                if (result.IsFailure)
                {
                    ConnectorsMetrics.ReconciliationErrors.Add(
                        1,
                        new KeyValuePair<string, object?>("provider", providerTag)
                    );
                    logger.LogWarning(
                        "ReconciliationJob could not reconcile account {AccountId} ({Provider}): {Error}",
                        account.Id,
                        providerTag,
                        result.Error.Message
                    );
                    continue;
                }

                var outcome = result.Value;
                if (outcome.Skipped || outcome.MessagesFound == 0)
                    continue;

                ConnectorsMetrics.ReconciliationMessagesFound.Add(
                    outcome.MessagesFound,
                    new KeyValuePair<string, object?>("provider", providerTag)
                );

                // Gmail/Graph son push-primary — un pase que NO sembró el cursor por primera vez y
                // aun así encontró mensajes significa que el push no los entregó. IMAP no tiene push
                // en absoluto: acá reconciliación ES el mecanismo de sync, así que encontrar mensajes
                // es simplemente... mail llegando, no una recuperación de nada.
                var isPushBackedProvider = account.ProviderCode is ProviderCode.Gmail or ProviderCode.Graph;
                if (isPushBackedProvider && !outcome.CursorWasSeeded)
                {
                    accountsWithRecoveredMessages++;
                    ConnectorsMetrics.ReconciliationMessagesRecovered.Add(
                        outcome.MessagesFound,
                        new KeyValuePair<string, object?>("provider", providerTag)
                    );
                    logger.LogWarning(
                        "Reconciliation recovered {Count} message(s) for account {AccountId} ({Provider}) that the push path had not delivered — investigate webhook/watch health for this account.",
                        outcome.MessagesFound,
                        account.Id,
                        providerTag
                    );
                }
            }
        }

        logger.LogInformation(
            "ReconciliationJob scanned {Scanned}/{Total} active accounts, recovered missed messages on {Recovered} account(s).",
            scanned,
            accounts.Count,
            accountsWithRecoveredMessages
        );
    }
}
