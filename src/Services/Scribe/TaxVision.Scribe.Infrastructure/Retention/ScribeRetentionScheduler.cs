using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Scribe.Application.Templates;

namespace TaxVision.Scribe.Infrastructure.Retention;

/// <summary>
/// Fase 10 — job diario que purga (elimina definitivamente) versiones de EmailTemplate en estado
/// Archived más antiguas que la política de retención (default 180 días, deshabilitado por
/// default). Deliberadamente NO purga <c>EmailLayoutVersion</c>: una versión Published de un
/// template puede seguir pinneada a un layout ya Archived (el pin es por número de versión, no
/// "latest") — purgarla rompería el render de esa versión. Un template Archived nunca se renderiza
/// (el pipeline solo busca Published), así que purgar sus versiones es seguro; lo mismo no vale
/// para layouts sin antes verificar que ningún template todavía los referencia.
/// </summary>
public sealed class ScribeRetentionScheduler(
    IServiceScopeFactory scopeFactory,
    IOptions<ScribeRetentionOptions> options,
    ILogger<ScribeRetentionScheduler> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(10);

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
            logger.LogError(ex, "ScribeRetentionScheduler iteration failed.");
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        var opt = options.Value;
        if (!opt.Enabled)
            return;

        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IEmailTemplateRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var cutoff = DateTime.UtcNow.AddDays(-opt.RetentionDays);
        var candidates = await repository.GetWithArchivedVersionsOlderThanAsync(cutoff, opt.BatchSize, ct);
        if (candidates.Count == 0)
            return;

        var purgedCount = 0;
        foreach (var template in candidates)
            purgedCount += template.PurgeArchivedVersionsOlderThan(cutoff).Count;

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "ScribeRetentionScheduler purged {PurgedCount} archived template versions across {TemplateCount} templates older than {Cutoff}.",
            purgedCount,
            candidates.Count,
            cutoff
        );
    }
}
