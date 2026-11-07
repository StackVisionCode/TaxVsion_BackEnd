using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.Layouts;
using TaxVision.Scribe.Domain.Templates;
using TaxVision.Scribe.Domain.ValueObjects;
using TaxVision.Scribe.Infrastructure.Persistence.Repositories;

namespace TaxVision.Scribe.Tests.Integration;

/// <summary>SCR-09 contra SQL real: cada render trae solo lo que usa, sin seguimiento.</summary>
[Collection(ScribeTempDatabaseCollection.Name)]
public sealed class PublishedTemplateReaderIntegrationTests(ScribeTempDatabase database)
{
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Returns_only_published_version_untracked()
    {
        var key = NewKey();
        var published = await SeedTemplateAsync(key, TemplateScope.System, null, versions: 20);
        await using var db = database.NewContext();

        var template = await new PublishedTemplateReader(db).GetByKeyAsync(key, null);

        Assert.Equal(published, template.Value.Version!.Id);
        Assert.Equal("Subject 19", template.Value.Version.Subject);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    // El render lee el preheader de estas definiciones: sin cargarlas, caía siempre al asunto.
    [Fact]
    public async Task Brings_the_variable_definitions_the_render_reads()
    {
        var key = NewKey();
        await SeedTemplateAsync(key, TemplateScope.System, null, versions: 2);
        await using var db = database.NewContext();

        var template = await new PublishedTemplateReader(db).GetByKeyAsync(key, null);

        var preheader = Assert.Single(template.Value.Version!.VariableDefinitions);
        Assert.Equal("Default preheader", preheader.DefaultValue);
    }

    [Fact]
    public async Task Tenant_override_wins_over_the_system_template()
    {
        var key = NewKey();
        var tenantId = Guid.NewGuid();
        await SeedTemplateAsync(key, TemplateScope.System, null, versions: 1);
        var tenantVersion = await SeedTemplateAsync(key, TemplateScope.Tenant, tenantId, versions: 1);
        await using var db = database.NewContext();

        var template = await new PublishedTemplateReader(db).GetByKeyAsync(key, tenantId);

        Assert.Equal(tenantId, template.Value.TenantId);
        Assert.Equal(tenantVersion, template.Value.Version!.Id);
    }

    [Fact]
    public async Task Another_tenant_falls_back_to_the_system_template()
    {
        var key = NewKey();
        var systemVersion = await SeedTemplateAsync(key, TemplateScope.System, null, versions: 1);
        await SeedTemplateAsync(key, TemplateScope.Tenant, Guid.NewGuid(), versions: 1);
        await using var db = database.NewContext();

        var template = await new PublishedTemplateReader(db).GetByKeyAsync(key, Guid.NewGuid());

        Assert.Null(template.Value.TenantId);
        Assert.Equal(systemVersion, template.Value.Version!.Id);
    }

    // Igual que antes: un override sin publicar da "sin versión publicada", no cae a la del sistema.
    [Fact]
    public async Task Tenant_override_without_published_version_stays_the_override()
    {
        var key = NewKey();
        var tenantId = Guid.NewGuid();
        await SeedTemplateAsync(key, TemplateScope.System, null, versions: 1);
        await SeedTemplateAsync(key, TemplateScope.Tenant, tenantId, versions: 1, publish: false);
        await using var db = database.NewContext();

        var template = await new PublishedTemplateReader(db).GetByKeyAsync(key, tenantId);

        Assert.Equal(tenantId, template.Value.TenantId);
        Assert.Null(template.Value.Version);
    }

    [Fact]
    public async Task Unknown_key_is_not_found()
    {
        await using var db = database.NewContext();

        var template = await new PublishedTemplateReader(db).GetByKeyAsync(NewKey(), null);

        Assert.Equal("EmailTemplate.NotFound", template.Error.Code);
    }

    [Fact]
    public async Task Pinned_layout_brings_only_that_version_untracked()
    {
        var layoutId = await SeedLayoutAsync(versions: 5);
        await using var db = database.NewContext();
        var reader = new PublishedTemplateReader(db);

        var pinned = await reader.GetPinnedLayoutAsync(layoutId, 3);
        var missing = await reader.GetPinnedLayoutAsync(layoutId, 99);

        Assert.Equal(3, pinned.Value.Version!.VersionNumber);
        Assert.Null(missing.Value.Version);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Unknown_layout_is_not_found()
    {
        await using var db = database.NewContext();

        var layout = await new PublishedTemplateReader(db).GetPinnedLayoutAsync(Guid.NewGuid(), 1);

        Assert.Equal("EmailLayout.NotFound", layout.Error.Code);
    }

    private static TemplateKey NewKey() => TemplateKey.Create($"it.reader.{Guid.NewGuid():N}").Value;

    private async Task<Guid> SeedTemplateAsync(
        TemplateKey key,
        TemplateScope scope,
        Guid? tenantId,
        int versions,
        bool publish = true
    )
    {
        var template = EmailTemplate.CreateNew(scope, tenantId, key, "Template", null, Guid.NewGuid(), Now).Value;
        EmailTemplateVersion last = null!;
        for (var i = 0; i < versions; i++)
            last = template
                .AddDraftVersion(
                    $"Subject {i}",
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
                    [("preheader", VariableType.String, false, "Default preheader", null)],
                    Now
                )
                .Value;
        if (publish)
            Assert.True(template.PublishVersion(last.Id, Guid.NewGuid(), Now).IsSuccess);

        await using var seed = database.NewContext();
        seed.EmailTemplates.Add(template);
        await seed.SaveChangesAsync();
        return last.Id;
    }

    private async Task<Guid> SeedLayoutAsync(int versions)
    {
        var layout = EmailLayout
            .CreateNew(
                TemplateScope.System,
                null,
                LayoutKey.Create($"it-layout-{Guid.NewGuid():N}").Value,
                "Layout",
                null,
                Guid.NewGuid(),
                Now
            )
            .Value;
        for (var i = 0; i < versions; i++)
            layout.AddDraftVersion("key", Guid.NewGuid(), null, null, null, null, Now);

        await using var seed = database.NewContext();
        seed.EmailLayouts.Add(layout);
        await seed.SaveChangesAsync();
        return layout.Id;
    }
}
