using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.TenantDomains;
using TaxVision.Auth.Infrastructure.Persistence;

namespace TaxVision.Auth.Api.Jobs;

/// <summary>
/// Fase A5/A6/A7 — re-consulta cada 5 minutos los custom hostnames en Provisioning y
/// los pasa a Active en cuanto Cloudflare confirma status=active &amp; ssl.status=active
/// (o a Failed si Cloudflare los bloqueó), sin que el tenant admin tenga que hacer
/// clic en "verificar". PUT .../activate sigue disponible para que lo confirme
/// manualmente sin esperar el siguiente ciclo. domain.MarkActive/MarkFailed encolan el
/// domain event correspondiente; AuthDbContext.SaveChangesAsync lo audita y publica —
/// el poller ya no necesita saber nada de auditoría ni de integration events (Fase A7).
/// </summary>
public sealed class TenantDomainProvisioningPoller(
    IServiceScopeFactory scopeFactory,
    ILogger<TenantDomainProvisioningPoller> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Tenant domain provisioning poll failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var domains = scope.ServiceProvider.GetRequiredService<ITenantDomainRepository>();
        var cloudflare = scope.ServiceProvider.GetRequiredService<ICloudflareProvisioningClient>();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        var pending = await domains.GetProvisioningCustomHostnamesAsync(ct);
        if (pending.Count == 0)
            return;

        var transitioned = 0;
        foreach (var domain in pending)
            transitioned += await TryAdvanceAsync(domain, cloudflare, ct) ? 1 : 0;

        if (transitioned > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Tenant domain provisioning poll: {Transitioned}/{Checked} hostname(s) changed state.",
                transitioned,
                pending.Count
            );
        }
    }

    private async Task<bool> TryAdvanceAsync(
        TenantDomain domain,
        ICloudflareProvisioningClient cloudflare,
        CancellationToken ct
    )
    {
        if (domain.CloudflareCustomHostnameId is not { } cloudflareId)
            return false;

        var statusResult = await cloudflare.GetCustomHostnameAsync(cloudflareId, ct);
        if (statusResult.IsFailure)
        {
            logger.LogWarning(
                "Could not poll Cloudflare status for TenantDomain {DomainId}: {Error}",
                domain.Id,
                statusResult.Error.Message
            );
            return false;
        }

        var status = statusResult.Value;
        if (status.IsFullyActive)
            return domain.MarkActive(DateTime.UtcNow).IsSuccess;

        if (status.IsBlocked)
            return domain.MarkFailed("cloudflare_blocked").IsSuccess;

        return false;
    }
}
