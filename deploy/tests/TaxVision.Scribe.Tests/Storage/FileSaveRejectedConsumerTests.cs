using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Scribe.Application.Storage;
using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.Layouts;
using TaxVision.Scribe.Domain.Projections;
using TaxVision.Scribe.Domain.Templates;
using TaxVision.Scribe.Domain.ValueObjects;
using TaxVision.Scribe.Infrastructure.Persistence;
using TaxVision.Scribe.Infrastructure.Persistence.Repositories;
using TaxVision.Scribe.Tests.Projections;

namespace TaxVision.Scribe.Tests.Storage;

/// <summary>
/// Contra un ScribeDbContext InMemory con los repositorios reales: así también se prueba la query
/// que busca qué versiones apuntan al FileId rechazado.
/// </summary>
public sealed class FileSaveRejectedConsumerTests
{
    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    private readonly ScribeDbContext _dbContext = new(
        new DbContextOptionsBuilder<ScribeDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new NoTenantContext()
    );

    private readonly MemoryCache _l1Cache = new(new MemoryCacheOptions { SizeLimit = 1000 });

    [Fact]
    public async Task Handle_removes_the_header_logo_that_still_points_to_the_rejected_file()
    {
        var rejectedFileId = Guid.NewGuid();
        await SeedHeaderLogoAsync(rejectedFileId);
        _l1Cache.Set("logo:system", "cached", new MemoryCacheEntryOptions { Size = 1 });

        await HandleAsync(Rejected(rejectedFileId));

        // Sin fila = "sin sembrar": LogoResolver sale sin logo y el seeder lo vuelve a sembrar.
        Assert.Null(await new SystemAssetRefRepository(_dbContext).GetByKeyAsync(SystemAssetKeys.HeaderLogo));
        Assert.False(_l1Cache.TryGetValue("logo:system", out _));
    }

    [Fact]
    public async Task Handle_keeps_a_header_logo_that_already_points_to_a_newer_file()
    {
        var newerFileId = Guid.NewGuid();
        await SeedHeaderLogoAsync(newerFileId);

        await HandleAsync(Rejected(Guid.NewGuid()));

        var stored = await new SystemAssetRefRepository(_dbContext).GetByKeyAsync(SystemAssetKeys.HeaderLogo);
        Assert.Equal(newerFileId, stored!.CloudStorageFileId);
    }

    [Fact]
    public async Task Handle_releases_optional_text_and_design_of_a_draft_template()
    {
        var rejectedFileId = Guid.NewGuid();
        var template = NewEmailTemplate();
        var draft = AddEmailDraft(template, htmlFileId: Guid.NewGuid(), textFileId: rejectedFileId, designFileId: null);
        var designDraft = AddEmailDraft(
            template,
            htmlFileId: Guid.NewGuid(),
            textFileId: null,
            designFileId: rejectedFileId
        );
        await SaveAsync(template);

        await HandleAsync(Rejected(rejectedFileId));

        // Sin texto, el render lo deriva del HTML (mismo camino que una versión sin texto).
        Assert.Null(draft.TextFileId);
        Assert.Null(draft.TextStorageKey);
        Assert.Null(designDraft.DesignJsonFileId);
        Assert.Null(designDraft.DesignJsonStorageKey);
    }

    [Fact]
    public async Task Handle_leaves_a_newer_file_and_the_required_html_untouched()
    {
        var rejectedFileId = Guid.NewGuid();
        var newerTextFileId = Guid.NewGuid();
        var template = NewEmailTemplate();
        var blocked = AddEmailDraft(template, htmlFileId: rejectedFileId, textFileId: null, designFileId: null);
        var newer = AddEmailDraft(
            template,
            htmlFileId: Guid.NewGuid(),
            textFileId: newerTextFileId,
            designFileId: null
        );
        await SaveAsync(template);

        await HandleAsync(Rejected(rejectedFileId));

        // El HTML es obligatorio: la Draft queda sin poder publicarse (solo se avisa en el log).
        Assert.Equal(rejectedFileId, blocked.HtmlFileId);
        Assert.Equal(newerTextFileId, newer.TextFileId);
        Assert.Equal(new[] { blocked.VersionNumber }, template.DraftsBlockedByRejectedFile(rejectedFileId));
    }

    [Fact]
    public async Task Handle_does_not_touch_published_versions()
    {
        var rejectedFileId = Guid.NewGuid();
        var template = NewEmailTemplate();
        var published = AddEmailDraft(
            template,
            htmlFileId: Guid.NewGuid(),
            textFileId: null,
            designFileId: rejectedFileId
        );
        template.PublishVersion(published.Id, Guid.NewGuid(), DateTime.UtcNow);
        await SaveAsync(template);

        await HandleAsync(Rejected(rejectedFileId));

        Assert.Equal(rejectedFileId, published.DesignJsonFileId);
    }

    [Fact]
    public async Task Handle_keeps_the_text_of_a_push_draft_because_it_is_required()
    {
        var rejectedFileId = Guid.NewGuid();
        var template = EmailTemplate
            .CreateNew(
                TemplateScope.System,
                null,
                TemplateKey.Create("push.reminder").Value,
                "Push reminder",
                null,
                Guid.NewGuid(),
                DateTime.UtcNow,
                TemplateChannel.Push
            )
            .Value;
        var draft = template.AddPushDraftVersion("Title", "text-key", rejectedFileId, [], DateTime.UtcNow).Value;
        await SaveAsync(template);

        await HandleAsync(Rejected(rejectedFileId));

        Assert.Equal(rejectedFileId, draft.TextFileId);
        Assert.Equal(new[] { draft.VersionNumber }, template.DraftsBlockedByRejectedFile(rejectedFileId));
    }

    [Fact]
    public async Task Handle_releases_the_design_of_a_draft_layout()
    {
        var rejectedFileId = Guid.NewGuid();
        var layout = EmailLayout
            .CreateNew(
                TemplateScope.Tenant,
                Guid.NewGuid(),
                LayoutKey.Create("office-base").Value,
                "Office base",
                null,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        var draft = layout
            .AddDraftVersion("html-key", Guid.NewGuid(), "design-key", rejectedFileId, null, null, DateTime.UtcNow)
            .Value;
        await _dbContext.EmailLayouts.AddAsync(layout);
        await _dbContext.SaveChangesAsync();

        await HandleAsync(Rejected(rejectedFileId));

        Assert.Null(draft.DesignJsonFileId);
        Assert.Null(draft.DesignJsonStorageKey);
    }

    [Fact]
    public async Task Handle_ignores_rejections_of_files_requested_by_another_service()
    {
        var rejectedFileId = Guid.NewGuid();
        await SeedHeaderLogoAsync(rejectedFileId);
        var template = NewEmailTemplate();
        var draft = AddEmailDraft(template, htmlFileId: Guid.NewGuid(), textFileId: rejectedFileId, designFileId: null);
        await SaveAsync(template);

        await HandleAsync(Rejected(rejectedFileId) with { RequestingService = "signature" });

        Assert.NotNull(await new SystemAssetRefRepository(_dbContext).GetByKeyAsync(SystemAssetKeys.HeaderLogo));
        Assert.Equal(rejectedFileId, draft.TextFileId);
    }

    [Fact]
    public async Task Handle_is_a_no_op_for_an_unknown_file_and_for_a_redelivery()
    {
        var rejectedFileId = Guid.NewGuid();
        var template = NewEmailTemplate();
        var draft = AddEmailDraft(template, htmlFileId: Guid.NewGuid(), textFileId: rejectedFileId, designFileId: null);
        await SaveAsync(template);

        await HandleAsync(Rejected(Guid.NewGuid()));
        Assert.Equal(rejectedFileId, draft.TextFileId);

        await HandleAsync(Rejected(rejectedFileId));
        var redelivery = await Record.ExceptionAsync(() => HandleAsync(Rejected(rejectedFileId)));

        Assert.Null(redelivery);
        Assert.Null(draft.TextFileId);
    }

    private Task HandleAsync(FileSaveRejectedIntegrationEvent evt) =>
        FileSaveRejectedConsumer.Handle(
            evt,
            new EmailTemplateRepository(_dbContext),
            new EmailLayoutRepository(_dbContext),
            new SystemAssetRefRepository(_dbContext),
            _dbContext,
            _l1Cache,
            new FakeCorrelationContext(),
            NullLogger<FileSaveRejectedIntegrationEvent>.Instance,
            CancellationToken.None
        );

    private static FileSaveRejectedIntegrationEvent Rejected(Guid fileId) =>
        new()
        {
            TenantId = PlatformTenant.Id,
            FileId = fileId,
            RequestingService = "scribe",
            OwnerType = "Tenant",
            FolderType = "Templates",
            Reason = "UploadPolicy",
        };

    private async Task SeedHeaderLogoAsync(Guid fileId)
    {
        await _dbContext.SystemAssetRefs.AddAsync(
            SystemAssetRef.Create(SystemAssetKeys.HeaderLogo, fileId, "image/png", 2048, DateTime.UtcNow)
        );
        await _dbContext.SaveChangesAsync();
    }

    private async Task SaveAsync(EmailTemplate template)
    {
        await _dbContext.EmailTemplates.AddAsync(template);
        await _dbContext.SaveChangesAsync();
    }

    private static EmailTemplate NewEmailTemplate() =>
        EmailTemplate
            .CreateNew(
                TemplateScope.Tenant,
                Guid.NewGuid(),
                TemplateKey.Create("office.welcome").Value,
                "Office welcome",
                null,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;

    private static EmailTemplateVersion AddEmailDraft(
        EmailTemplate template,
        Guid htmlFileId,
        Guid? textFileId,
        Guid? designFileId
    ) =>
        template
            .AddDraftVersion(
                "Subject",
                "html-key",
                htmlFileId,
                textFileId is null ? null : "text-key",
                textFileId,
                designFileId is null ? null : "design-key",
                designFileId,
                null,
                null,
                Guid.NewGuid(),
                1,
                [],
                DateTime.UtcNow
            )
            .Value;
}
