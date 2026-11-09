using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.Layouts;
using TaxVision.Scribe.Domain.Templates;
using TaxVision.Scribe.Domain.ValueObjects;

namespace TaxVision.Scribe.Tests.Integration;

/// <summary>SCR-03 contra SQL real: dos escritores de la misma plantilla o layout ya no se pisan en silencio.</summary>
[Collection(ScribeTempDatabaseCollection.Name)]
public sealed class EmailTemplateConcurrencyIntegrationTests(ScribeTempDatabase database)
{
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Override_scope_and_deprecate_in_parallel_second_save_conflicts()
    {
        var id = await SeedTemplateAsync();
        await using var first = database.NewContext();
        await using var second = database.NewContext();
        var overriding = await LoadTemplateAsync(first, id);
        var deprecating = await LoadTemplateAsync(second, id);

        Assert.True(overriding.OverrideDispatchScope("System", Guid.NewGuid(), Now).IsSuccess);
        await first.SaveChangesAsync();
        Assert.True(deprecating.DeprecateTemplate(Now.AddSeconds(1)).IsSuccess);

        var conflict = await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());
        Assert.Equal("Persistence.ConcurrencyConflict", conflict.Code);
    }

    // Una versión nueva toca la raíz: si no, la versión agregada y la baja del template no se verían.
    [Fact]
    public async Task Adding_a_version_conflicts_with_a_parallel_deprecate()
    {
        var id = await SeedTemplateAsync();
        await using var first = database.NewContext();
        await using var second = database.NewContext();
        var adding = await LoadTemplateAsync(first, id);
        var deprecating = await LoadTemplateAsync(second, id);

        Assert.True(AddDraft(adding).IsSuccess);
        await first.SaveChangesAsync();
        Assert.True(deprecating.DeprecateTemplate(Now.AddSeconds(1)).IsSuccess);

        await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Layout_deprecate_and_new_version_in_parallel_second_save_conflicts()
    {
        var layout = EmailLayout
            .CreateNew(
                TemplateScope.System,
                null,
                LayoutKey.Create($"layout-{Guid.NewGuid():N}"[..20]).Value,
                "Layout",
                null,
                PlatformTenant.Id,
                Now
            )
            .Value;
        await using (var seed = database.NewContext())
        {
            seed.EmailLayouts.Add(layout);
            await seed.SaveChangesAsync();
        }

        await using var first = database.NewContext();
        await using var second = database.NewContext();
        var adding = await first.EmailLayouts.Include(l => l.Versions).SingleAsync(l => l.Id == layout.Id);
        var deprecating = await second.EmailLayouts.Include(l => l.Versions).SingleAsync(l => l.Id == layout.Id);

        Assert.True(adding.AddDraftVersion("key", Guid.NewGuid(), null, null, null, null, Now).IsSuccess);
        await first.SaveChangesAsync();
        Assert.True(deprecating.DeprecateLayout(Now.AddSeconds(1)).IsSuccess);

        await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());
    }

    private async Task<Guid> SeedTemplateAsync()
    {
        var template = EmailTemplate
            .CreateNew(
                TemplateScope.System,
                null,
                TemplateKey.Create($"it.template_{Guid.NewGuid():N}.v1").Value,
                "Template",
                null,
                PlatformTenant.Id,
                Now
            )
            .Value;
        await using var seed = database.NewContext();
        seed.EmailTemplates.Add(template);
        await seed.SaveChangesAsync();
        return template.Id;
    }

    private static Task<EmailTemplate> LoadTemplateAsync(Infrastructure.Persistence.ScribeDbContext db, Guid id) =>
        db.EmailTemplates.Include(t => t.Versions).SingleAsync(t => t.Id == id);

    private static Result<EmailTemplateVersion> AddDraft(EmailTemplate template) =>
        template.AddDraftVersion(
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
            Now
        );
}
