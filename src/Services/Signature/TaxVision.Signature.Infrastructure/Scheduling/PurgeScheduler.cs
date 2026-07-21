using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Infrastructure.Scheduling;

public sealed class PurgeSchedulerOptions
{
    public const string SectionName = "Signature:Purge";

    /// <summary>Habilita o desactiva el purge (default: false — se activa cuando el negocio lo autorice).</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Años de retención desde la última actualización antes de considerar candidato.</summary>
    public int RetentionYears { get; set; } = 7;

    /// <summary>Tamaño de lote por iteración para no saturar la BD.</summary>
    public int BatchSize { get; set; } = 100;
}

/// <summary>
/// Job diario que purga solicitudes en estado terminal cuya última actualización es más
/// antigua que la política de retención (default 7 años — IRS §6501) y que no tienen
/// <c>LegalHold</c> activo. La eliminación es en cascada (Signers, Fields, Challenges,
/// ConsentEvents, AuditEvents) — el ORM maneja los FK cascade.
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

        using var scope = serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISignatureRequestRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var cutoff = DateTime.UtcNow.AddYears(-opt.RetentionYears);
        var batch = await repository.ListPurgeCandidatesAsync(cutoff, opt.BatchSize, ct);
        if (batch.Count == 0)
            return;

        foreach (var request in batch)
            repository.Remove(request);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "PurgeScheduler removed {Count} signature requests older than {Cutoff}.",
            batch.Count,
            cutoff
        );
    }
}
