using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.Templates;
using TaxVision.Scribe.Domain.ValueObjects;
using TaxVision.Scribe.Infrastructure.Persistence;
using TaxVision.Scribe.Infrastructure.Persistence.Repositories;

namespace TaxVision.Scribe.Tests.Persistence;

/// <summary>
/// GetAllPublishedAsync (Fase 6, usado por TemplateWarmupService) es la primera query real con
/// filtro sobre la colección hija — se prueba contra un ScribeDbContext InMemory de verdad (no un
/// Fake) para atrapar problemas de traducción LINQ que un Fake no puede detectar.
/// </summary>
public sealed class EmailTemplateRepositoryGetAllPublishedAsyncTests
{
    // NoTenantContext: el único método bajo prueba acá, GetAllPublishedAsync, usa
    // IgnoreQueryFilters() (job cross-tenant, RBAC Fase 5) — el filtro global nunca entra en juego.
    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    private static ScribeDbContext BuildContext() =>
        new(
            new DbContextOptionsBuilder<ScribeDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenantContext()
        );

    private static EmailTemplate BuildTemplateWithVersions(string keyValue, params EmailVersionStatus[] statuses)
    {
        var template = EmailTemplate
            .CreateNew(
                TemplateScope.System,
                null,
                TemplateKey.Create(keyValue).Value,
                keyValue,
                null,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;

        foreach (var status in statuses)
        {
            var version = template
                .AddDraftVersion(
                    "Subject",
                    "html-key",
                    Guid.NewGuid(),
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    Guid.NewGuid(),
                    1,
                    [],
                    DateTime.UtcNow
                )
                .Value;
            if (status == EmailVersionStatus.Published)
                template.PublishVersion(version.Id, Guid.NewGuid(), DateTime.UtcNow);
        }

        return template;
    }

    [Fact]
    public async Task GetAllPublishedAsync_returns_only_templates_that_have_a_published_version()
    {
        await using var dbContext = BuildContext();
        var published = BuildTemplateWithVersions("with.published", EmailVersionStatus.Published);
        var draftOnly = BuildTemplateWithVersions("draft.only", EmailVersionStatus.Draft);
        await dbContext.EmailTemplates.AddRangeAsync(published, draftOnly);
        await dbContext.SaveChangesAsync();

        var repository = new EmailTemplateRepository(dbContext);
        var result = await repository.GetAllPublishedAsync();

        var entry = Assert.Single(result);
        Assert.Equal("with.published", entry.Template.TemplateKey.Value);
        Assert.Equal(EmailVersionStatus.Published, entry.Version.Status);
    }

    [Fact]
    public async Task GetAllPublishedAsync_returns_the_published_version_not_an_archived_one_on_the_same_template()
    {
        await using var dbContext = BuildContext();
        var template = BuildTemplateWithVersions("multi.version", EmailVersionStatus.Draft);
        var firstVersion = template.Versions[0];
        template.PublishVersion(firstVersion.Id, Guid.NewGuid(), DateTime.UtcNow);
        var secondVersion = template
            .AddDraftVersion(
                "Subject v2",
                "html-key-v2",
                Guid.NewGuid(),
                null,
                null,
                null,
                null,
                null,
                null,
                Guid.NewGuid(),
                1,
                [],
                DateTime.UtcNow
            )
            .Value;
        template.PublishVersion(secondVersion.Id, Guid.NewGuid(), DateTime.UtcNow);
        await dbContext.EmailTemplates.AddAsync(template);
        await dbContext.SaveChangesAsync();

        var repository = new EmailTemplateRepository(dbContext);
        var result = await repository.GetAllPublishedAsync();

        var entry = Assert.Single(result);
        Assert.Equal(secondVersion.Id, entry.Version.Id);
        Assert.Equal(EmailVersionStatus.Archived, firstVersion.Status);
    }

    [Fact]
    public async Task GetAllPublishedAsync_returns_empty_when_no_template_has_a_published_version()
    {
        await using var dbContext = BuildContext();
        var draftOnly = BuildTemplateWithVersions("draft.only", EmailVersionStatus.Draft);
        await dbContext.EmailTemplates.AddAsync(draftOnly);
        await dbContext.SaveChangesAsync();

        var repository = new EmailTemplateRepository(dbContext);
        var result = await repository.GetAllPublishedAsync();

        Assert.Empty(result);
    }
}
