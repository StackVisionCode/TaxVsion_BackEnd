using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Domain.Templates;
using TaxVision.Signature.Infrastructure.Persistence;
using TaxVision.Signature.Infrastructure.Persistence.Queries;

namespace TaxVision.Signature.Tests.Integration;

/// <summary>
/// Qué archivos son PDF base de una plantilla, contra el SQL Server real: el base de la plantilla y los documentos
/// que añade, nunca los de otro tenant ni archivos ajenos a plantillas.
/// </summary>
public sealed class SignatureTemplateFileQueryIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _otherTenantId = Guid.NewGuid();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var tenantId in new[] { _tenantId, _otherTenantId })
        {
            await using var db = NewContext(tenantId);
            await db.SignatureTemplates.IgnoreQueryFilters().Where(t => t.TenantId == tenantId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Only_the_base_and_document_files_of_the_tenant_templates_are_template_files()
    {
        var baseFile = Guid.NewGuid();
        var addedFile = Guid.NewGuid();
        var otherTenantFile = Guid.NewGuid();
        var ownFile = Guid.NewGuid();
        var template = Template(_tenantId, baseFile);
        Assert.True(template.AddDocument(TestSignatureLimits.Default, addedFile, "Annex").IsSuccess);
        await SeedAsync(_tenantId, template);
        await SeedAsync(_otherTenantId, Template(_otherTenantId, otherTenantFile));
        await using var db = NewContext(_tenantId);

        var found = await new SignatureTemplateFileQuery(db).TemplateBaseFilesAsync(
            _tenantId,
            [baseFile, addedFile, otherTenantFile, ownFile]
        );

        Assert.Equal(new HashSet<Guid> { baseFile, addedFile }, found.ToHashSet());
    }

    private static SignatureTemplate Template(Guid tenantId, Guid baseFile) =>
        SignatureTemplate
            .CreateDraft(
                TestSignatureLimits.Default,
                tenantId,
                Guid.NewGuid(),
                "Engagement letter",
                null,
                "Fiscal",
                72,
                false,
                false,
                false,
                baseDocumentFileId: baseFile
            )
            .Value;

    private static async Task SeedAsync(Guid tenantId, SignatureTemplate template)
    {
        await using var db = NewContext(tenantId);
        db.SignatureTemplates.Add(template);
        await db.SaveChangesAsync();
    }

    private static SignatureDbContext NewContext(Guid tenantId) =>
        new(
            new DbContextOptionsBuilder<SignatureDbContext>()
                .UseSqlServer(SignatureTestDatabase.ConnectionString)
                .Options,
            new FixedTenant(tenantId)
        );

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }
}
