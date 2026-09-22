using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Infrastructure.Scheduling;

public sealed class DraftRetentionSchedulerOptions
{
    public const string SectionName = "Signature:DraftRetention";

    /// <summary>Habilita o desactiva la retención de borradores (default: true).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Días sin tocar un borrador (Draft/Ready) antes de borrarlo (default: 30, como DocuSign).</summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>Tamaño de lote por iteración para no saturar la BD.</summary>
    public int BatchSize { get; set; } = 100;
}

/// <summary>
/// Job diario que borra en firme los borradores sin enviar (Draft/Ready) que no se tocan
/// desde hace más de <c>RetentionDays</c> y no tienen <c>LegalHold</c>. Los borradores no
/// expiran por el reloj de firma (ese sólo corre tras el envío); esta es su única limpieza.
/// La eliminación es en cascada (Signers, Fields, etc.) — el ORM maneja los FK cascade.
/// </summary>
public sealed class DraftRetentionScheduler(
    IServiceProvider serviceProvider,
    IOptions<DraftRetentionSchedulerOptions> options,
    ILogger<DraftRetentionScheduler> logger
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
            logger.LogError(ex, "DraftRetentionScheduler iteration failed.");
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

        var cutoff = DateTime.UtcNow.AddDays(-opt.RetentionDays);
        var batch = await repository.ListStaleUnsentAsync(cutoff, opt.BatchSize, ct);
        if (batch.Count == 0)
            return;

        foreach (var request in batch)
            repository.Remove(request);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "DraftRetentionScheduler removed {Count} unsent drafts untouched since {Cutoff}.",
            batch.Count,
            cutoff
        );
    }
}
