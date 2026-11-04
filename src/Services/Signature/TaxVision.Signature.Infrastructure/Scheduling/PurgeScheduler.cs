using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Limits;

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
/// Deshabilitado por default para no perder data por accidente en dev. En producción se
/// activa con <c>Signature:Purge:Enabled = true</c> tras validar con legal.
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
        using var scope = serviceProvider.CreateScope();
        var policy = await scope
            .ServiceProvider.GetRequiredService<ISignatureLimitsResolver>()
            .ResolveAsync(tenantId, ct);
        if (!policy.PurgeEnabled)
            return;

        var repository = scope.ServiceProvider.GetRequiredService<ISignatureRequestRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var cutoff = DateTime.UtcNow.AddYears(-policy.RetentionYears);
        var batch = await repository.ListPurgeCandidatesAsync(tenantId, cutoff, batchSize, ct);
        if (batch.Count == 0)
            return;

        foreach (var request in batch)
            repository.Remove(request);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "PurgeScheduler removed {Count} signature requests of tenant {TenantId} older than {Cutoff}.",
            batch.Count,
            tenantId,
            cutoff
        );
    }
}
