using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.OAuth;

namespace TaxVision.Connectors.Infrastructure.Jobs;

/// <summary>
/// Cada 5 minutos busca cuentas cuyo access token expira en menos de 10 minutos y dispara su
/// refresh vía <see cref="IOAuthTokenManager"/>. No necesita lock propio: el manager ya trae su
/// lock por-cuenta (Redis, TTL 30s) — si otro nodo ya refrescó, el double-check adentro lo detecta
/// y este job simplemente recibe el token ya vigente sin volver a pegarle al provider.
/// </summary>
public sealed class ProactiveTokenRefreshJob(IServiceProvider serviceProvider, ILogger<ProactiveTokenRefreshJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ExpiringWithin = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lifetime = serviceProvider.GetRequiredService<IHostApplicationLifetime>();
        await lifetime.WaitForApplicationStartedAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceSafeAsync(stoppingToken);
            try
            {
                await Task.Delay(Interval, stoppingToken);
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
            logger.LogError(ex, "ProactiveTokenRefreshJob iteration failed.");
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var connectionRepository = scope.ServiceProvider.GetRequiredService<IOAuthConnectionRepository>();

        var threshold = DateTime.UtcNow.Add(ExpiringWithin);
        var accountIds = await connectionRepository.ListAccountIdsWithTokenExpiringBeforeAsync(threshold, ct);
        if (accountIds.Count == 0)
            return;

        var refreshed = 0;
        foreach (var accountId in accountIds)
        {
            if (await RefreshOneAsync(accountId, ct))
                refreshed++;
        }

        logger.LogInformation(
            "ProactiveTokenRefreshJob refreshed {Refreshed}/{Total} accounts.",
            refreshed,
            accountIds.Count
        );
    }

    // Scope propio por cuenta: reusar un DbContext en un loop largo acumula tracking innecesario. El proveedor se
    // llama sin transacción; el rechazo se confirma en su propia unidad (IMailboxFailureUnit). Un error en una
    // cuenta no corta el barrido.
    private async Task<bool> RefreshOneAsync(Guid accountId, CancellationToken ct)
    {
        using var accountScope = serviceProvider.CreateScope();
        var tokenManager = accountScope.ServiceProvider.GetRequiredService<IOAuthTokenManager>();
        var correlation = accountScope.ServiceProvider.GetRequiredService<ICorrelationContext>();

        using (correlation.Push(Guid.NewGuid().ToString("N")))
        {
            try
            {
                var result = await tokenManager.GetValidAccessTokenAsync(accountId, ct);
                if (result.IsSuccess)
                    return true;

                logger.LogWarning(
                    "ProactiveTokenRefreshJob could not refresh account {AccountId}: {Error}",
                    accountId,
                    result.Error.Message
                );
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "ProactiveTokenRefreshJob failed on account {AccountId}.", accountId);
            }
            return false;
        }
    }
}
