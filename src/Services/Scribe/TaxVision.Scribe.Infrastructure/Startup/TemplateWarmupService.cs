using BuildingBlocks.Infrastructure.Hosting;
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
/// <para>
/// A diferencia de los 3 seeders de este mismo ensamblado, este NO extendía
/// <see cref="DeferredStartupHostedService"/> — corría en <c>IHostedService.StartAsync</c> sin
/// esperar a <c>ApplicationStarted</c>. Pedía un token M2M a auth-api (vía
/// <see cref="Storage.ServiceTokenAcquirer"/>) y templates a cloudstorage-api en ese punto exacto
/// del arranque del host, antes incluso de que Wolverine terminara de inicializar — si además
/// auth-api todavía no estaba escuchando en su puerto (carrera de arranque de contenedores en
/// producción, confirmada en logs reales: "Connection refused (auth-api:8080)" repetido 13 veces),
/// el warm-up completo fallaba en silencio (0/13 templates cacheados) sin tumbar el host. Migrado a
/// la misma base que los seeders para eliminar la carrera de raíz.
/// </para>
/// </summary>
public sealed class TemplateWarmupService(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    ILogger<TemplateWarmupService> logger
) : DeferredStartupHostedService(lifetime, logger)
{
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
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
}
