using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Domain.Templates;
using TaxVision.Signature.Infrastructure.Persistence;

namespace TaxVision.Signature.Tests.Integration;

/// <summary>SIG-5 contra SQL real: dos editores de la misma plantilla no se pisan. Base temporal propia.</summary>
public sealed class SignatureTemplateConcurrencyIntegrationTests : IAsyncLifetime
{
    private readonly SignatureTemporaryDatabase _database = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    public Task InitializeAsync() => _database.CreateAsync();

    public Task DisposeAsync() => _database.DropAsync();

    [Fact]
    public async Task TwoEditorsSameTemplate_SecondSaveConflicts()
    {
        var templateId = await SeedAsync();
        await using var first = _database.NewContext(_tenantId);
        await using var second = _database.NewContext(_tenantId);
        var mine = await LoadAsync(first, templateId);
        var theirs = await LoadAsync(second, templateId);

        Assert.True(mine.UpdateMetadata("First editor", null, "Fiscal").IsSuccess);
        Assert.True(theirs.UpdateMetadata("Second editor", null, "Fiscal").IsSuccess);
        await first.SaveChangesAsync();

        var conflict = await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());

        Assert.Equal("Persistence.ConcurrencyConflict", conflict.Code);
        await using var read = _database.NewContext(_tenantId);
        Assert.Equal("First editor", (await LoadAsync(read, templateId)).Title);
    }

    // Un hijo nuevo también choca: la raíz se toca aunque el cambio sea solo del documento.
    [Fact]
    public async Task ChildChangeVsMetadataEdit_SecondSaveConflicts()
    {
        var templateId = await SeedAsync();
        await using var first = _database.NewContext(_tenantId);
        await using var second = _database.NewContext(_tenantId);
        var mine = await LoadAsync(first, templateId);
        var theirs = await LoadAsync(second, templateId);

        Assert.True(mine.AddDocument(TestSignatureLimits.Default, Guid.NewGuid(), "Return").IsSuccess);
        Assert.True(theirs.UpdateMetadata("Second editor", null, "Fiscal").IsSuccess);
        await first.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());
    }

    private async Task<Guid> SeedAsync()
    {
        var template = SignatureTemplate
            .CreateDraft(
                TestSignatureLimits.Default,
                _tenantId,
                Guid.NewGuid(),
                "Template",
                null,
                "Fiscal",
                72,
                false,
                false,
                false
            )
            .Value;
        await using var db = _database.NewContext(_tenantId);
        db.SignatureTemplates.Add(template);
        await db.SaveChangesAsync();
        return template.Id;
    }

    private static Task<SignatureTemplate> LoadAsync(SignatureDbContext db, Guid templateId) =>
        db.SignatureTemplates.IgnoreQueryFilters().Include(t => t.Documents).SingleAsync(t => t.Id == templateId);
}
