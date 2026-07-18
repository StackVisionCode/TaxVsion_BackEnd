using BuildingBlocks.Common;
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
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ExpiringWithin = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(StartupDelay, stoppingToken);
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

    private async Task RunOnceAsync(CancellationToken ct)
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
            // Scope propio por cuenta: cada refresh hace su propio SaveChanges vía IUnitOfWork
            // (dentro de OAuthTokenManager) — reusar un DbContext entre iteraciones de un loop
            // largo acumula tracking de aggregates ya persistidos, innecesario acá.
            using var accountScope = serviceProvider.CreateScope();
            var tokenManager = accountScope.ServiceProvider.GetRequiredService<IOAuthTokenManager>();
            var correlation = accountScope.ServiceProvider.GetRequiredService<ICorrelationContext>();

            using (correlation.Push(Guid.NewGuid().ToString("N")))
            {
                var result = await tokenManager.GetValidAccessTokenAsync(accountId, ct);
                if (result.IsSuccess)
                {
                    refreshed++;
                }
                else
                {
                    logger.LogWarning(
                        "ProactiveTokenRefreshJob could not refresh account {AccountId}: {Error}",
                        accountId,
                        result.Error.Message
                    );
                }
            }
        }

        logger.LogInformation(
            "ProactiveTokenRefreshJob refreshed {Refreshed}/{Total} accounts.",
            refreshed,
            accountIds.Count
        );
    }
}
