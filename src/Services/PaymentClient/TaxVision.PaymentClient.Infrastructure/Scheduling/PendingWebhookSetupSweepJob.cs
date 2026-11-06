using BuildingBlocks.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.TenantPaymentConfigs.Commands.RegisterTenantPaymentWebhook;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Infrastructure.Persistence;

namespace TaxVision.PaymentClient.Infrastructure.Scheduling;

/// <summary>
/// Rescata registros automáticos de webhook que quedaron en <c>Pending</c> sin comando (caída entre guardar y
/// encolar). Solo toca los pedidos más viejos que <see cref="PaymentWebhookOptions.StalePendingAfter"/>, que
/// supera todo el ciclo de reintentos, así que no compite con uno en curso; al re-encolar renueva la marca
/// para no dispararlo dos veces. Con la función apagada esos pendientes pasan a <c>Failed</c>.
/// </summary>
public sealed class PendingWebhookSetupSweepJob(
    IServiceScopeFactory scopeFactory,
    IDistributedLockFactory lockFactory,
    IOptions<PaymentWebhookOptions> options,
    ILogger<PendingWebhookSetupSweepJob> logger
)
    : PeriodicPaymentClientJob(
        scopeFactory,
        lockFactory,
        logger,
        TimeSpan.FromMinutes(options.Value.PendingSweepIntervalMinutes),
        TimeSpan.FromMinutes(options.Value.PendingSweepIntervalMinutes)
    )
{
    protected override string JobName => "pending-webhook-setup-sweep";

    protected override Task RunOnceAsync(IServiceProvider services, CancellationToken ct) =>
        SweepAsync(
            services.GetRequiredService<ITenantPaymentConfigRepository>(),
            scopeFactory,
            options.Value,
            logger,
            DateTime.UtcNow,
            ct
        );

    /// <summary>Una vuelta: lee los pendientes y procesa cada uno en su unidad (BB-3), donde la marca y el
    /// comando se confirman juntos. Público para probarlo con fakes.</summary>
    public static async Task<PendingWebhookSetupSweepSummary> SweepAsync(
        ITenantPaymentConfigRepository configs,
        IServiceScopeFactory scopeFactory,
        PaymentWebhookOptions settings,
        ILogger logger,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var requestedBeforeUtc = nowUtc - settings.StalePendingAfter;
        var stale = await configs.GetStalePendingWebhookSetupsAsync(
            requestedBeforeUtc,
            settings.PendingSweepBatchSize,
            ct
        );
        if (stale.Count == 0)
            return new PendingWebhookSetupSweepSummary(0, 0);

        int requeued = 0,
            failed = 0;
        foreach (var (tenantId, provider) in stale.Select(config => (config.TenantId, config.ProviderCode)).ToList())
        {
            var renewed = false;
            var result = await TransactionalJobUnit.RunAsync<PaymentClientDbContext>(
                scopeFactory,
                async (unit, token) =>
                {
                    // Se relee en la unidad: otro escritor pudo resolverlo o renovarlo después de la lectura.
                    var config = await unit
                        .Services.GetRequiredService<ITenantPaymentConfigRepository>()
                        .GetByTenantAndProviderAsync(tenantId, provider, token);
                    if (config is null || !IsStale(config, requestedBeforeUtc))
                        return false;

                    if (!settings.IsAutoSetupEnabled)
                        return config.FailAutomaticWebhookSetup(nowUtc).IsSuccess;

                    if (config.RenewWebhookSetupRequest(nowUtc).IsFailure)
                        return false;

                    unit.Outbox.TenantId = tenantId.ToString();
                    await unit.PublishAsync(new RegisterTenantPaymentWebhookCommand(tenantId, provider));
                    renewed = true;
                    return true;
                },
                logger,
                ct
            );

            if (result.Outcome != JobUnitOutcome.Committed)
                continue;
            if (renewed)
                requeued++;
            else
                failed++;
        }

        logger.LogInformation(
            "Pending webhook setup sweep re-queued {Requeued} and failed {Failed} stale setup(s).",
            requeued,
            failed
        );
        return new PendingWebhookSetupSweepSummary(requeued, failed);
    }

    private static bool IsStale(TenantPaymentConfig config, DateTime requestedBeforeUtc) =>
        config.WebhookSetup == WebhookSetupStatus.Pending
        && (config.WebhookSetupRequestedAtUtc is null || config.WebhookSetupRequestedAtUtc < requestedBeforeUtc);
}

public sealed record PendingWebhookSetupSweepSummary(int Requeued, int Failed);
