using BuildingBlocks.Results;
using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.Templates;
using TaxVision.Scribe.Domain.ValueObjects;

namespace TaxVision.Scribe.Tests.Templates;

public sealed class EmailTemplateTests
{
    private static readonly IReadOnlyList<(
        string Name,
        VariableType Type,
        bool Required,
        string? DefaultValue,
        string? Description
    )> NoVariables = [];

    private static TemplateKey ValidKey(string value = "auth.password_reset") => TemplateKey.Create(value).Value;

    private static EmailTemplate CreateSystemTemplate() =>
        EmailTemplate
            .CreateNew(
                TemplateScope.System,
                tenantId: null,
                ValidKey(),
                "Password reset",
                description: null,
                createdByUserId: Guid.NewGuid(),
                createdAtUtc: DateTime.UtcNow
            )
            .Value;

    private static Result<EmailTemplateVersion> AddDraftVersion(
        EmailTemplate template,
        string subject,
        string htmlStorageKey,
        Guid layoutId,
        int layoutVersionNumber = 1,
        IReadOnlyList<(
            string Name,
            VariableType Type,
            bool Required,
            string? DefaultValue,
            string? Description
        )>? variables = null
    ) =>
        template.AddDraftVersion(
            subject,
            htmlStorageKey,
            Guid.NewGuid(),
            textStorageKey: null,
            textFileId: null,
            designJsonStorageKey: null,
            designJsonFileId: null,
            previewImageStorageKey: null,
            previewImageFileId: null,
            layoutId,
            layoutVersionNumber,
            variables ?? NoVariables,
            DateTime.UtcNow
        );

    [Fact]
    public void CreateNew_rejects_tenant_scope_without_tenant_id()
    {
        var result = EmailTemplate.CreateNew(
            TemplateScope.Tenant,
            tenantId: null,
            ValidKey(),
            "Name",
            description: null,
            createdByUserId: Guid.NewGuid(),
            createdAtUtc: DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal("EmailTemplate.TenantRequired", result.Error.Code);
    }

    [Fact]
    public void CreateNew_rejects_system_scope_with_tenant_id()
    {
        var result = EmailTemplate.CreateNew(
            TemplateScope.System,
            tenantId: Guid.NewGuid(),
            ValidKey(),
            "Name",
            description: null,
            createdByUserId: Guid.NewGuid(),
            createdAtUtc: DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal("EmailTemplate.TenantNotAllowed", result.Error.Code);
    }

    [Fact]
    public void CreateNew_succeeds_with_active_status()
    {
        var template = CreateSystemTemplate();

        Assert.Equal(EmailContentStatus.Active, template.Status);
        Assert.Empty(template.Versions);
    }

    [Fact]
    public void AddDraftVersion_assigns_incremental_version_numbers()
    {
        var template = CreateSystemTemplate();

        var first = AddDraftVersion(
            template,
            "Subject",
            "system/templates/auth.password_reset/v1/template.html",
            Guid.NewGuid()
        );
        var second = AddDraftVersion(
            template,
            "Subject",
            "system/templates/auth.password_reset/v2/template.html",
            Guid.NewGuid()
        );

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(1, first.Value.VersionNumber);
        Assert.Equal(2, second.Value.VersionNumber);
        Assert.Equal(2, template.Versions.Count);
    }

    [Fact]
    public void AddDraftVersion_rejects_missing_layout()
    {
        var template = CreateSystemTemplate();

        var result = AddDraftVersion(
            template,
            "Subject",
            "system/templates/auth.password_reset/v1/template.html",
            Guid.Empty
        );

        Assert.True(result.IsFailure);
        Assert.Equal("EmailTemplateVersion.LayoutRequired", result.Error.Code);
    }

    [Fact]
    public void AddDraftVersion_rejects_duplicate_variable_names()
    {
        var template = CreateSystemTemplate();
        var variables = new List<(
            string Name,
            VariableType Type,
            bool Required,
            string? DefaultValue,
            string? Description
        )>
        {
            ("code", VariableType.String, true, null, null),
            ("CODE", VariableType.String, false, null, null),
        };

        var result = AddDraftVersion(
            template,
            "Subject",
            "system/templates/auth.otp_code/v1/template.html",
            Guid.NewGuid(),
            variables: variables
        );

        Assert.True(result.IsFailure);
        Assert.Equal("EmailTemplateVersion.DuplicateVariable", result.Error.Code);
    }

    [Fact]
    public void AddDraftVersion_rejects_when_template_is_not_active()
    {
        var template = CreateSystemTemplate();
        template.DeprecateTemplate(DateTime.UtcNow);

        var result = AddDraftVersion(
            template,
            "Subject",
            "system/templates/auth.password_reset/v1/template.html",
            Guid.NewGuid()
        );

        Assert.True(result.IsFailure);
        Assert.Equal("EmailTemplate.NotActive", result.Error.Code);
    }

    [Fact]
    public void PublishVersion_publishes_draft_and_archives_previously_published_sibling()
    {
        var template = CreateSystemTemplate();
        var v1 = AddDraftVersion(
            template,
            "Subject",
            "system/templates/auth.password_reset/v1/template.html",
            Guid.NewGuid()
        ).Value;
        var publisherId = Guid.NewGuid();
        var publishV1 = template.PublishVersion(v1.Id, publisherId, DateTime.UtcNow);
        Assert.True(publishV1.IsSuccess);
        Assert.Equal(EmailVersionStatus.Published, v1.Status);

        var v2 = AddDraftVersion(
            template,
            "Subject v2",
            "system/templates/auth.password_reset/v2/template.html",
            Guid.NewGuid()
        ).Value;
        var publishV2 = template.PublishVersion(v2.Id, publisherId, DateTime.UtcNow);

        Assert.True(publishV2.IsSuccess);
        Assert.Equal(EmailVersionStatus.Published, v2.Status);
        Assert.Equal(EmailVersionStatus.Archived, v1.Status);
    }

    [Fact]
    public void PublishVersion_fails_when_version_does_not_belong_to_template()
    {
        var template = CreateSystemTemplate();

        var result = template.PublishVersion(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("EmailTemplate.VersionNotFound", result.Error.Code);
    }

    [Fact]
    public void PurgeArchivedVersionsOlderThan_removes_only_archived_versions_past_the_cutoff()
    {
        var template = CreateSystemTemplate();
        var oldArchived = template
            .AddDraftVersion(
                "Subject v1",
                "system/templates/auth.password_reset/v1/template.html",
                Guid.NewGuid(),
                null,
                null,
                null,
                null,
                null,
                null,
                Guid.NewGuid(),
                1,
                NoVariables,
                DateTime.UtcNow.AddDays(-200)
            )
            .Value;
        template.PublishVersion(oldArchived.Id, Guid.NewGuid(), DateTime.UtcNow.AddDays(-200));

        var recentDraft = AddDraftVersion(
            template,
            "Subject v2",
            "system/templates/auth.password_reset/v2/template.html",
            Guid.NewGuid()
        ).Value;
        // Publicar v2 archiva automáticamente v1 (invariante "solo una Published a la vez").
        template.PublishVersion(recentDraft.Id, Guid.NewGuid(), DateTime.UtcNow);

        Assert.Equal(EmailVersionStatus.Archived, oldArchived.Status);
        Assert.Equal(EmailVersionStatus.Published, recentDraft.Status);

        var purgedIds = template.PurgeArchivedVersionsOlderThan(DateTime.UtcNow.AddDays(-90));

        Assert.Single(purgedIds);
        Assert.Equal(oldArchived.Id, purgedIds[0]);
        Assert.Single(template.Versions);
        Assert.Equal(recentDraft.Id, template.Versions[0].Id);
    }

    [Fact]
    public void PurgeArchivedVersionsOlderThan_never_removes_published_or_draft_versions()
    {
        var template = CreateSystemTemplate();
        var published = AddDraftVersion(
            template,
            "Subject v1",
            "system/templates/auth.password_reset/v1/template.html",
            Guid.NewGuid()
        ).Value;
        template.PublishVersion(published.Id, Guid.NewGuid(), DateTime.UtcNow);

        // Draft nunca se toca aunque sea "vieja": solo Archived es candidata.
        var draft = template
            .AddDraftVersion(
                "Subject v2",
                "system/templates/auth.password_reset/v2/template.html",
                Guid.NewGuid(),
                null,
                null,
                null,
                null,
                null,
                null,
                Guid.NewGuid(),
                1,
                NoVariables,
                DateTime.UtcNow.AddDays(-200)
            )
            .Value;

        var purgedIds = template.PurgeArchivedVersionsOlderThan(DateTime.UtcNow.AddDays(1));

        Assert.Empty(purgedIds);
        Assert.Equal(2, template.Versions.Count);
        Assert.Equal(EmailVersionStatus.Published, published.Status);
        Assert.Equal(EmailVersionStatus.Draft, draft.Status);
    }

    [Fact]
    public void DeprecateTemplate_fails_when_already_deprecated()
    {
        var template = CreateSystemTemplate();
        template.DeprecateTemplate(DateTime.UtcNow);

        var result = template.DeprecateTemplate(DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("EmailTemplate.AlreadyDeprecated", result.Error.Code);
    }
}
