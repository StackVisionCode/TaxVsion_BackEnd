using BuildingBlocks.Results;
using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.Layouts;
using TaxVision.Scribe.Domain.ValueObjects;

namespace TaxVision.Scribe.Tests.Layouts;

public sealed class EmailLayoutTests
{
    private static LayoutKey ValidKey(string value = "system-base") => LayoutKey.Create(value).Value;

    private static EmailLayout CreateSystemLayout() =>
        EmailLayout
            .CreateNew(
                TemplateScope.System,
                tenantId: null,
                ValidKey(),
                "System base",
                description: null,
                createdByUserId: Guid.NewGuid(),
                createdAtUtc: DateTime.UtcNow
            )
            .Value;

    private static Result<EmailLayoutVersion> AddDraftVersion(EmailLayout layout, string htmlStorageKey) =>
        layout.AddDraftVersion(
            htmlStorageKey,
            Guid.NewGuid(),
            designJsonStorageKey: null,
            designJsonFileId: null,
            previewImageStorageKey: null,
            previewImageFileId: null,
            DateTime.UtcNow
        );

    [Fact]
    public void CreateNew_rejects_tenant_scope_without_tenant_id()
    {
        var result = EmailLayout.CreateNew(
            TemplateScope.Tenant,
            tenantId: null,
            ValidKey("tenant-base"),
            "Tenant base",
            description: null,
            createdByUserId: Guid.NewGuid(),
            createdAtUtc: DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal("EmailLayout.TenantRequired", result.Error.Code);
    }

    [Fact]
    public void AddDraftVersion_assigns_incremental_version_numbers()
    {
        var layout = CreateSystemLayout();

        var first = AddDraftVersion(layout, "system/layouts/system-base/v1/layout.html");
        var second = AddDraftVersion(layout, "system/layouts/system-base/v2/layout.html");

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(1, first.Value.VersionNumber);
        Assert.Equal(2, second.Value.VersionNumber);
    }

    [Fact]
    public void AddDraftVersion_rejects_empty_html_storage_key()
    {
        var layout = CreateSystemLayout();

        var result = AddDraftVersion(layout, "");

        Assert.True(result.IsFailure);
        Assert.Equal("EmailLayoutVersion.HtmlStorageKey", result.Error.Code);
    }

    [Fact]
    public void PublishVersion_publishes_draft_and_archives_previously_published_sibling()
    {
        var layout = CreateSystemLayout();
        var v1 = AddDraftVersion(layout, "system/layouts/system-base/v1/layout.html").Value;
        var publisherId = Guid.NewGuid();
        layout.PublishVersion(v1.Id, publisherId, DateTime.UtcNow);

        var v2 = AddDraftVersion(layout, "system/layouts/system-base/v2/layout.html").Value;
        var publishV2 = layout.PublishVersion(v2.Id, publisherId, DateTime.UtcNow);

        Assert.True(publishV2.IsSuccess);
        Assert.Equal(EmailVersionStatus.Published, v2.Status);
        Assert.Equal(EmailVersionStatus.Archived, v1.Status);
    }

    [Fact]
    public void DeprecateLayout_fails_when_already_deprecated()
    {
        var layout = CreateSystemLayout();
        layout.DeprecateLayout(DateTime.UtcNow);

        var result = layout.DeprecateLayout(DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("EmailLayout.AlreadyDeprecated", result.Error.Code);
    }
}
