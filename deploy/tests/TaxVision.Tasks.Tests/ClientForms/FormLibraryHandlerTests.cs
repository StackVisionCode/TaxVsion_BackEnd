using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.DataCollection.Engine;
using TaxVision.Tasks.ClientForms.Application.Library;
using TaxVision.Tasks.ClientForms.Application.Library.Commands;
using TaxVision.Tasks.ClientForms.Application.Library.Queries;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Audit;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.ClientForms.Domain.ValueObjects;
using TaxVision.Tasks.ClientForms.Domain.Versions;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class FormLibraryHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("40000000-0000-0000-0000-0000000000f1");
    private static readonly Guid UserId = Guid.Parse("40000000-0000-0000-0000-0000000000f2");
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    private const string LibraryJson = FormTemplateHandlerTestDoubles.ValidDefinition;

    [Fact]
    public async Task Clone_creates_an_independent_tenant_template_with_library_origin()
    {
        var detail = new LibraryTemplateDetailRecord(
            Guid.NewGuid(),
            "individual-tax-intake",
            "Individual Tax Intake",
            "Collect client details.",
            FormPurpose.ClientIntake,
            3,
            LibraryJson
        );
        var templates = new MemoryFormTemplateRepository();
        var audit = new RecordingFormAuditLog();

        var result = await CloneLibraryTemplateHandler.Handle(
            new CloneLibraryTemplateCommand(TenantId, UserId, "individual-tax-intake", false),
            new MemoryFormLibraryRepository(detail),
            FormTemplateHandlerTestDoubles.Engine(),
            templates,
            audit,
            new RecordingUnitOfWork(),
            default
        );

        Assert.True(result.IsSuccess);
        var created = Assert.Single(templates.Items);
        Assert.Equal(TenantId, created.TenantId);
        Assert.NotNull(created.LibraryOrigin);
        Assert.Equal("individual-tax-intake", created.LibraryOrigin!.LibraryKey);
        Assert.Equal(detail.SourceTemplateId, created.LibraryOrigin.SourceTemplateId);
        Assert.Equal(3, created.LibraryOrigin.SourceVersionNumber);
        Assert.Equal(0, created.LatestVersionNumber);
        Assert.False(created.IsLibraryListed);
        Assert.Equal([FormAuditAction.TemplateClonedFromLibrary], audit.Actions);
    }

    [Fact]
    public async Task Clone_fails_when_the_key_is_not_in_the_library()
    {
        var result = await CloneLibraryTemplateHandler.Handle(
            new CloneLibraryTemplateCommand(TenantId, UserId, "missing", false),
            new MemoryFormLibraryRepository(),
            FormTemplateHandlerTestDoubles.Engine(),
            new MemoryFormTemplateRepository(),
            new RecordingFormAuditLog(),
            new RecordingUnitOfWork(),
            default
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FormLibraryErrors.NotFound, result.Error);
    }

    // Decisión del dueño (2026-10-28): listar ya no exige revisión, solo una versión publicada.
    [Fact]
    public async Task Listing_a_published_version_works_without_a_review()
    {
        var (template, version) = PublishedPlatformTemplate();
        var result = await SetLibraryListingHandler.Handle(
            new SetLibraryListingCommand(UserId, template.Id, "individual-tax-intake", true),
            new MemoryFormTemplateRepository(template),
            new MemoryFormTemplateVersionRepository(version),
            new RecordingFormAuditLog(),
            new RecordingUnitOfWork(),
            default
        );

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsLibraryListed);
    }

    [Fact]
    public void Listing_still_needs_a_published_version()
    {
        var template = FormTemplateHandlerTestDoubles.Template(PlatformTenant.Id);

        var result = template.ListInLibrary("individual-tax-intake", PlatformTenant.Id, Now);

        Assert.Equal(FormLibraryErrors.NotPublished, result.Error);
    }

    // Si una semilla no pasa la validación de publicación, el seeder la salta y la biblioteca queda vacía.
    [Fact]
    public void Every_library_seed_is_publishable()
    {
        var engine = FormTemplateHandlerTestDoubles.Engine();
        foreach (var seed in LibrarySeedCatalog.All())
        {
            var parsed = engine.ParseDefinition(seed.DefinitionJson);
            Assert.True(parsed.IsSuccess, seed.LibraryKey);
            var report = engine.ValidateForPublish(parsed.Value);
            Assert.True(
                report.IsValid,
                $"{seed.LibraryKey}: {string.Join(", ", report.Issues.Select(issue => issue.Code))}"
            );
        }
    }

    [Fact]
    public void Seed_publisher_publishes_the_draft_and_lists_it()
    {
        var template = FormTemplateHandlerTestDoubles.Template(PlatformTenant.Id);
        template.MarkAsLibrary("individual-tax-intake", PlatformTenant.Id);

        var version = LibrarySeedPublisher.PublishAndList(
            FormTemplateHandlerTestDoubles.Engine(),
            template,
            1,
            PlatformTenant.Id,
            UserId,
            Now
        );

        Assert.True(version.IsSuccess);
        Assert.Equal(1, template.LatestVersionNumber);
        Assert.True(template.IsLibraryListed);
    }

    [Fact]
    public async Task Review_then_list_lists_the_template()
    {
        var (template, version) = PublishedPlatformTemplate();
        var templates = new MemoryFormTemplateRepository(template);
        var versions = new MemoryFormTemplateVersionRepository(version);
        var audit = new RecordingFormAuditLog();

        var review = await RecordLibraryReviewHandler.Handle(
            new RecordLibraryReviewCommand(UserId, template.Id, 1, "EA", "Looks good"),
            templates,
            versions,
            audit,
            new RecordingUnitOfWork(),
            default
        );
        Assert.True(review.IsSuccess);

        var listed = await SetLibraryListingHandler.Handle(
            new SetLibraryListingCommand(UserId, template.Id, "individual-tax-intake", true),
            templates,
            versions,
            audit,
            new RecordingUnitOfWork(),
            default
        );

        Assert.True(listed.IsSuccess);
        Assert.True(listed.Value.IsLibraryListed);
        Assert.Equal("individual-tax-intake", listed.Value.LibraryKey);
        Assert.Equal([FormAuditAction.LibraryReviewed, FormAuditAction.LibraryListed], audit.Actions);
    }

    [Fact]
    public async Task Review_rejects_a_version_that_is_not_the_latest_published()
    {
        var (template, version) = PublishedPlatformTemplate();
        var result = await RecordLibraryReviewHandler.Handle(
            new RecordLibraryReviewCommand(UserId, template.Id, 2, "CPA", null),
            new MemoryFormTemplateRepository(template),
            new MemoryFormTemplateVersionRepository(version),
            new RecordingFormAuditLog(),
            new RecordingUnitOfWork(),
            default
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FormLibraryErrors.ReviewVersionMismatch, result.Error);
    }

    [Fact]
    public void MarkAsLibrary_is_rejected_outside_the_platform_tenant()
    {
        var template = FormTemplateHandlerTestDoubles.Template(TenantId);
        var result = template.MarkAsLibrary("individual-tax-intake", PlatformTenant.Id);
        Assert.True(result.IsFailure);
        Assert.Equal(FormLibraryErrors.PlatformOnly, result.Error);
    }

    [Fact]
    public async Task Preview_returns_the_reviewed_definition()
    {
        var detail = new LibraryTemplateDetailRecord(
            Guid.NewGuid(),
            "individual-tax-intake",
            "Individual Tax Intake",
            null,
            FormPurpose.ClientIntake,
            2,
            LibraryJson
        );
        var result = await GetLibraryTemplateHandler.Handle(
            new GetLibraryTemplateQuery("individual-tax-intake"),
            new MemoryFormLibraryRepository(detail),
            default
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.VersionNumber);
        Assert.Equal(LibraryJson, result.Value.Definition);
    }

    // Plantilla del tenant de plataforma con una versión publicada (LatestVersionNumber = 1).
    private static (FormTemplate Template, FormTemplateVersion Version) PublishedPlatformTemplate()
    {
        var engine = FormTemplateHandlerTestDoubles.Engine();
        var definition = engine.ParseDefinition(LibraryJson).Value;
        var canonical = engine.Canonicalize(definition);
        var template = FormTemplate
            .Create(
                PlatformTenant.Id,
                FormTemplateName.Create("Individual Tax Intake").Value,
                FormDescription.Create("Collect client details.").Value,
                FormDefinitionDocument.Create(canonical.Json, FormDefinitionDocument.MaxBytes).Value,
                ContentHash.Create(canonical.Sha256).Value,
                FormPurpose.ClientIntake,
                UserId,
                Now
            )
            .Value;
        var version = template.Publish(canonical, definition.Title, FormPresentation.Wizard, 1, UserId, Now).Value;
        return (template, version);
    }
}

internal sealed class MemoryFormLibraryRepository(params LibraryTemplateDetailRecord[] seed) : IFormLibraryRepository
{
    internal List<LibraryTemplateDetailRecord> Items { get; } = [.. seed];

    public Task<IReadOnlyList<LibraryTemplateRecord>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LibraryTemplateRecord>>(
            Items
                .Select(item => new LibraryTemplateRecord(
                    item.LibraryKey,
                    item.Name,
                    item.Description,
                    item.Purpose,
                    item.VersionNumber
                ))
                .ToArray()
        );

    public Task<LibraryTemplateDetailRecord?> GetByKeyAsync(string libraryKey, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(item => item.LibraryKey == libraryKey));
}
