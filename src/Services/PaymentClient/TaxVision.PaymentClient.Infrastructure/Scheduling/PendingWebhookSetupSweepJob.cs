using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.TenantPaymentConfigs.Commands.RegisterTenantPaymentWebhook;
using TaxVision.PaymentClient.Domain.ValueObjects;
using Wolverine;

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
            services.GetRequiredService<IUnitOfWork>(),
            services.GetRequiredService<IMessageBus>(),
            options.Value,
            logger,
            DateTime.UtcNow,
            ct
        );

    /// <summary>Una vuelta: marca, guarda y recién después encola. Público para probarlo con fakes.</summary>
    public static async Task<PendingWebhookSetupSweepSummary> SweepAsync(
        ITenantPaymentConfigRepository configs,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        PaymentWebhookOptions settings,
        ILogger logger,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var stale = await configs.GetStalePendingWebhookSetupsAsync(
            nowUtc - settings.StalePendingAfter,
            settings.PendingSweepBatchSize,
            ct
        );
        if (stale.Count == 0)
            return new PendingWebhookSetupSweepSummary(0, 0);

        var requeue = new List<(Guid TenantId, PaymentProviderCode Provider)>();
        var failed = 0;
        foreach (var config in stale)
        {
            if (!settings.IsAutoSetupEnabled)
            {
                if (config.FailAutomaticWebhookSetup(nowUtc).IsSuccess)
                    failed++;
            }
            else if (config.RenewWebhookSetupRequest(nowUtc).IsSuccess)
                requeue.Add((config.TenantId, config.ProviderCode));
        }

        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Otro escritor tocó alguna fila: sin marca guardada no se encola; la próxima vuelta las recoge.
            logger.LogWarning(ex, "Pending webhook setup sweep could not save; retrying next round.");
            return new PendingWebhookSetupSweepSummary(0, 0);
        }

        foreach (var (tenantId, provider) in requeue)
        {
            bus.TenantId = tenantId.ToString();
            await bus.PublishAsync(new RegisterTenantPaymentWebhookCommand(tenantId, provider));
        }

        logger.LogInformation(
            "Pending webhook setup sweep re-queued {Requeued} and failed {Failed} stale setup(s).",
            requeue.Count,
            failed
        );
        return new PendingWebhookSetupSweepSummary(requeue.Count, failed);
    }
}

public sealed record PendingWebhookSetupSweepSummary(int Requeued, int Failed);
