using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Jobs;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Limits;
using TaxVision.Signature.Infrastructure.Persistence;

namespace TaxVision.Signature.Infrastructure.Scheduling;

/// <summary>Interruptor operativo del job y tamaño del lote. Los años de retención son de cada tenant.</summary>
public sealed class PurgeSchedulerOptions
{
    public const string SectionName = "Signature:Purge";

    public bool Enabled { get; set; } = false;

    public int BatchSize { get; set; } = 100;
}

/// <summary>
/// Job diario que purga, tenant por tenant, las solicitudes en estado terminal más viejas que su retención y
/// sin <c>LegalHold</c>. Solo entra un tenant cuya oficina activó la purga y cuyo plan la permite; los años
/// son los de la oficina, nunca menos que el mínimo del plan. La eliminación es en cascada (Signers, Fields,
/// Challenges, ConsentEvents, AuditEvents) — el ORM maneja los FK cascade.
///
/// <para>
/// Apagado en código; encendido en appsettings.json desde 2026-11-07. Solo purga en las oficinas
/// que activaron la purga y cuyo plan la permite.
/// </para>
/// </summary>
public sealed class PurgeScheduler(
    IServiceProvider serviceProvider,
    IOptions<PurgeSchedulerOptions> options,
    ILogger<PurgeScheduler> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

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
            logger.LogError(ex, "PurgeScheduler iteration failed.");
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        var opt = options.Value;
        if (!opt.Enabled)
            return;

        IReadOnlyList<Guid> tenantIds;
        using (var scope = serviceProvider.CreateScope())
            tenantIds = await scope
                .ServiceProvider.GetRequiredService<ITenantSignatureSettingsRepository>()
                .ListTenantIdsWithPurgeEnabledAsync(ct);

        foreach (var tenantId in tenantIds)
        {
            // Un scope por tenant: un fallo en uno no deja cambios a medias en el siguiente.
            try
            {
                await PurgeTenantAsync(tenantId, opt.BatchSize, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "PurgeScheduler failed for tenant {TenantId}.", tenantId);
            }
        }
    }

    private async Task PurgeTenantAsync(Guid tenantId, int batchSize, CancellationToken ct)
    {
        DateTime cutoff;
        List<Guid> candidates;
        using (var scope = serviceProvider.CreateScope())
        {
            var policy = await scope
                .ServiceProvider.GetRequiredService<ISignatureLimitsResolver>()
                .ResolveAsync(tenantId, ct);
            if (!policy.PurgeEnabled)
                return;

            cutoff = DateTime.UtcNow.AddYears(-policy.RetentionYears);
            var batch = await scope
                .ServiceProvider.GetRequiredService<ISignatureRequestRepository>()
                .ListPurgeCandidatesAsync(tenantId, cutoff, batchSize, ct);
            candidates = batch.Select(request => request.Id).ToList();
        }

        if (candidates.Count == 0)
            return;

        var summary = await PurgeAsync(tenantId, candidates, DateTime.UtcNow, ct);

        logger.LogInformation(
            "PurgeScheduler removed {Count} of {Total} signature requests of tenant {TenantId} older than {Cutoff}.",
            summary.Committed,
            summary.Total,
            tenantId,
            cutoff
        );
    }

    // SIG-10: una unidad por solicitud; el borrado y su aviso se confirman juntos o no queda ninguno.
    internal Task<JobBatchSummary> PurgeAsync(
        Guid tenantId,
        IReadOnlyList<Guid> requestIds,
        DateTime nowUtc,
        CancellationToken ct
    ) =>
        TransactionalJobUnit.RunEachAsync<Guid, SignatureDbContext>(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            requestIds,
            async (requestId, unit, token) =>
            {
                var repository = unit.Services.GetRequiredService<ISignatureRequestRepository>();
                var request = await repository.GetByIdAsync(tenantId, requestId, token);
                if (request is null || request.LegalHold)
                    return false;

                repository.Remove(request);
                await unit.PublishAsync(
                    new SignatureRequestPurgedIntegrationEvent
                    {
                        TenantId = tenantId,
                        CorrelationId = Guid.NewGuid().ToString("N"),
                        SignatureRequestId = request.Id,
                        PurgedAtUtc = nowUtc,
                    }
                );
                return true;
            },
            logger,
            ct
        );
}
