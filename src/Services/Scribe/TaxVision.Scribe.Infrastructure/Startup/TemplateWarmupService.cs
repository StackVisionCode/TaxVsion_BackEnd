using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Scribe.Application.Layouts;
using TaxVision.Scribe.Application.Rendering;
using TaxVision.Scribe.Application.Templates;

namespace TaxVision.Scribe.Infrastructure.Startup;

/// <summary>
/// Al arrancar, parsea y cachea (L1+L2) el body/text/layout de toda EmailTemplateVersion Published —
/// así el primer envío real de cada template no paga el round-trip a CloudStorage (Fase 6, plan §36
/// ítem 1). Vive en Infrastructure (no en Api/Startup como sugería literalmente el plan): toca
/// IEmailTemplateRepository/IEmailLayoutRepository/IEmailRenderer, que son abstracciones de
/// Application implementadas acá — mismo criterio que <see cref="Seed.ScribeBaseLayoutSeeder"/>.
/// </summary>
public sealed class TemplateWarmupService(IServiceScopeFactory scopeFactory, ILogger<TemplateWarmupService> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var templateRepository = scope.ServiceProvider.GetRequiredService<IEmailTemplateRepository>();
        var layoutRepository = scope.ServiceProvider.GetRequiredService<IEmailLayoutRepository>();
        var renderer = scope.ServiceProvider.GetRequiredService<IEmailRenderer>();

        var published = await templateRepository.GetAllPublishedAsync(cancellationToken);
        var warmed = 0;

        foreach (var (template, version) in published)
        {
            var layoutResult = await layoutRepository.GetByIdAsync(version.LayoutId, cancellationToken);
            if (layoutResult.IsFailure)
            {
                logger.LogWarning(
                    "Warm-up skipped template '{TemplateKey}' v{VersionNumber}: layout {LayoutId} not found.",
                    template.TemplateKey.Value,
                    version.VersionNumber,
                    version.LayoutId
                );
                continue;
            }

            var layoutVersion = layoutResult.Value.Versions.FirstOrDefault(v =>
                v.VersionNumber == version.LayoutVersionNumber
            );
            if (layoutVersion is null)
            {
                logger.LogWarning(
                    "Warm-up skipped template '{TemplateKey}' v{VersionNumber}: layout version {LayoutVersionNumber} not found.",
                    template.TemplateKey.Value,
                    version.VersionNumber,
                    version.LayoutVersionNumber
                );
                continue;
            }

            var warmupResult = await renderer.WarmupAsync(
                version,
                template.TemplateKey.Value,
                layoutVersion,
                layoutResult.Value.LayoutKey.Value,
                template.TenantId,
                cancellationToken
            );
            if (warmupResult.IsFailure)
            {
                logger.LogWarning(
                    "Warm-up failed for template '{TemplateKey}' v{VersionNumber}: {Error}",
                    template.TemplateKey.Value,
                    version.VersionNumber,
                    warmupResult.Error.Message
                );
                continue;
            }

            warmed++;
        }

        logger.LogInformation(
            "Scribe cache warm-up: {Warmed}/{Total} published template versions loaded into L1+L2.",
            warmed,
            published.Count
        );
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
