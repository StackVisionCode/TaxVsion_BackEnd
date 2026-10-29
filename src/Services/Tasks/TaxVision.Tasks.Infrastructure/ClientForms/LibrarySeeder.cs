using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.DataCollection.Engine;
using TaxVision.DataCollection.Engine.Mapping;
using TaxVision.Tasks.ClientForms.Application.Library;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.ClientForms.Domain.ValueObjects;
using TaxVision.Tasks.Infrastructure.Persistence;
using DomainPurpose = TaxVision.Tasks.ClientForms.Domain.Templates.FormPurpose;

namespace TaxVision.Tasks.Infrastructure.ClientForms;

// Al arrancar: crea las plantillas de biblioteca que falten en el tenant de plataforma. NUNCA sobrescribe
// contenido. Las que nunca se publicaron (sembradas antes, o nuevas) se publican y listan: los tenants las
// ven sin revisión previa; la plataforma las revisa y actualiza después desde el builder.
internal sealed class LibrarySeeder(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    ILogger<LibrarySeeder> logger
) : BackgroundService
{
    // Autor de sistema para las filas sembradas; no es una persona real.
    private static readonly Guid SeederUserId = new("8f58a521-4c25-4d91-9f4e-7ad5df14c901");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await lifetime.WaitForApplicationStartedAsync(stoppingToken);
            using var scope = scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
            var engine = scope.ServiceProvider.GetRequiredService<IFormEngine>();
            var catalogVersion = scope
                .ServiceProvider.GetRequiredService<IDataCollectionTargetCatalog>()
                .GetSnapshot()
                .CatalogVersion;

            var existing = await context
                .FormTemplates.IgnoreQueryFilters()
                .Where(template => template.TenantId == PlatformTenant.Id && template.LibraryKey != null)
                .ToListAsync(stoppingToken);
            var known = new HashSet<string>(existing.Select(template => template.LibraryKey!), StringComparer.Ordinal);

            var created = 0;
            var pending = existing.Where(template => template.LatestVersionNumber == 0).ToList();
            foreach (var seed in LibrarySeedCatalog.All())
            {
                if (known.Contains(seed.LibraryKey) || !TryBuild(engine, seed, out var template))
                    continue;
                context.FormTemplates.Add(template);
                pending.Add(template);
                created++;
            }

            var published = 0;
            foreach (var template in pending)
            {
                var version = LibrarySeedPublisher.PublishAndList(
                    engine,
                    template,
                    catalogVersion,
                    PlatformTenant.Id,
                    SeederUserId,
                    DateTime.UtcNow
                );
                if (version.IsFailure)
                {
                    logger.LogWarning(
                        "Library template {Key} could not be published: {Code}.",
                        template.LibraryKey,
                        version.Error.Code
                    );
                    continue;
                }
                template.ClearDomainEvents();
                version.Value.ClearDomainEvents();
                context.FormTemplateVersions.Add(version.Value);
                published++;
            }

            if (created > 0 || published > 0)
                await context.SaveChangesAsync(stoppingToken);
            logger.LogInformation(
                "Client form library seeder created {Created} and published {Published} template(s).",
                created,
                published
            );
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Client form library seeder failed; it runs again on the next start.");
        }
    }

    private bool TryBuild(IFormEngine engine, LibrarySeed seed, out FormTemplate template)
    {
        template = null!;
        var parsed = engine.ParseDefinition(seed.DefinitionJson);
        if (parsed.IsFailure)
        {
            logger.LogWarning("Library seed {Key} could not be parsed and was skipped.", seed.LibraryKey);
            return false;
        }

        var canonical = engine.Canonicalize(parsed.Value);
        var document = FormDefinitionDocument.Create(canonical.Json, engine.Limits.MaxDefinitionBytes);
        var hash = ContentHash.Create(canonical.Sha256);
        var name = FormTemplateName.Create(parsed.Value.Title);
        var description = FormDescription.Create(seed.Description);
        if (document.IsFailure || hash.IsFailure || name.IsFailure || description.IsFailure)
            return false;

        var created = FormTemplate.Create(
            PlatformTenant.Id,
            name.Value,
            description.Value,
            document.Value,
            hash.Value,
            (DomainPurpose)(int)parsed.Value.Purpose,
            SeederUserId,
            DateTime.UtcNow
        );
        if (created.IsFailure || created.Value.MarkAsLibrary(seed.LibraryKey, PlatformTenant.Id).IsFailure)
            return false;

        // El seeder no publica hechos: estas plantillas no son eventos de negocio de ningún tenant.
        created.Value.ClearDomainEvents();
        template = created.Value;
        return true;
    }
}
