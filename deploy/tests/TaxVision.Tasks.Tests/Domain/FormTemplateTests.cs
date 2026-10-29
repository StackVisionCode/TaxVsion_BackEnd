using TaxVision.DataCollection.Engine.Serialization;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.ClientForms.Domain.Templates.Events;
using TaxVision.Tasks.ClientForms.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Domain;

public sealed class FormTemplateTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
    private const string EmptyDefinition = "{\"components\":[],\"schemaVersion\":1}";

    [Fact]
    public void Create_sets_the_active_draft_and_raises_the_domain_event()
    {
        var template = NewTemplate();

        Assert.Equal(TenantId, template.TenantId);
        Assert.Equal(FormTemplateStatus.Active, template.Status);
        Assert.Equal(FormPurpose.ClientIntake, template.Purpose);
        Assert.Equal(EmptyDefinition, template.Draft.Json);
        Assert.Equal(0, template.LatestVersionNumber);
        Assert.Single(template.DomainEvents.OfType<FormTemplateCreated>());
    }

    [Fact]
    public void Create_rejects_an_empty_tenant_or_user()
    {
        var invalidTenant = Create(Guid.Empty, UserId);
        var invalidUser = Create(TenantId, Guid.Empty);

        Assert.Equal(FormTemplateErrors.InvalidOwner, invalidTenant.Error);
        Assert.Equal(FormTemplateErrors.InvalidOwner, invalidUser.Error);
    }

    [Fact]
    public void Publishing_creates_an_immutable_numbered_snapshot()
    {
        var template = NewTemplate();
        var canonical = Canonical(EmptyDefinition, 'a');

        var published = template.Publish(canonical, "Client intake", FormPresentation.Wizard, 0, UserId, Now);

        Assert.True(published.IsSuccess);
        Assert.Equal(1, published.Value.VersionNumber);
        Assert.Equal(template.Id, published.Value.FormTemplateId);
        Assert.Equal(canonical.Json, published.Value.Definition.Json);
        Assert.Equal(canonical.Sha256, published.Value.ContentHash.Value);
        Assert.Equal(published.Value.Id, template.LatestVersionId);
        Assert.Single(template.DomainEvents.OfType<FormTemplatePublished>());
    }

    [Fact]
    public void The_same_content_cannot_be_published_twice()
    {
        var template = NewTemplate();
        var canonical = Canonical(EmptyDefinition, 'b');
        template.Publish(canonical, "Version one", FormPresentation.SinglePage, 0, UserId, Now);

        var result = template.Publish(
            canonical,
            "Version two",
            FormPresentation.SinglePage,
            0,
            UserId,
            Now.AddMinutes(1)
        );

        Assert.Equal(FormTemplateErrors.NothingToPublish, result.Error);
        Assert.Equal(1, template.LatestVersionNumber);
    }

    [Fact]
    public void Publishing_distinct_content_advances_the_version_number()
    {
        var template = NewTemplate();
        template.Publish(Canonical(EmptyDefinition, 'c'), "Version one", FormPresentation.SinglePage, 0, UserId, Now);

        var second = template.Publish(
            Canonical("{\"components\":[{\"key\":\"name\"}],\"schemaVersion\":1}", 'd'),
            "Version two",
            FormPresentation.Wizard,
            1,
            UserId,
            Now.AddMinutes(1)
        );

        Assert.Equal(2, second.Value.VersionNumber);
        Assert.Equal(2, template.LatestVersionNumber);
    }

    [Fact]
    public void An_archived_template_is_read_only_until_restored()
    {
        var template = NewTemplate();
        template.Archive(UserId, Now);

        var rename = template.Rename(Name("Other name"), null, UserId, Now.AddMinutes(1));
        var publish = template.Publish(
            Canonical(EmptyDefinition, 'e'),
            "Archived",
            FormPresentation.SinglePage,
            0,
            UserId,
            Now
        );

        Assert.Equal(FormTemplateErrors.Archived, rename.Error);
        Assert.Equal(FormTemplateErrors.Archived, publish.Error);
        Assert.True(template.Restore(UserId, Now.AddMinutes(2)).IsSuccess);
        Assert.Equal(FormTemplateStatus.Active, template.Status);
        Assert.Null(template.ArchivedAtUtc);
    }

    [Fact]
    public void Restoring_an_active_template_is_rejected()
    {
        var result = NewTemplate().Restore(UserId, Now);

        Assert.Equal(FormTemplateErrors.NotArchived, result.Error);
    }

    [Fact]
    public void Revert_copies_the_published_snapshot_without_creating_a_version()
    {
        var template = NewTemplate();
        var published = template
            .Publish(Canonical(EmptyDefinition, 'f'), "Version one", FormPresentation.SinglePage, 0, UserId, Now)
            .Value;
        template.UpdateDraft(
            ClientFormsTestDocuments.Definition("{\"components\":[1],\"schemaVersion\":1}"),
            Hash('1'),
            FormPurpose.Questionnaire,
            UserId,
            Now.AddMinutes(1)
        );

        var reverted = template.RevertDraftTo(published, UserId, Now.AddMinutes(2));

        Assert.True(reverted.IsSuccess);
        Assert.Equal(published.Definition, template.Draft);
        Assert.Equal(published.ContentHash, template.DraftHash);
        Assert.Equal(1, template.LatestVersionNumber);
        Assert.Single(template.DomainEvents.OfType<FormTemplateDraftReverted>());
    }

    [Fact]
    public void Revert_rejects_a_version_from_another_template()
    {
        var source = NewTemplate();
        var target = NewTemplate();
        var version = source
            .Publish(Canonical(EmptyDefinition, '2'), "Source", FormPresentation.SinglePage, 0, UserId, Now)
            .Value;

        var result = target.RevertDraftTo(version, UserId, Now);

        Assert.Equal(FormTemplateErrors.VersionMismatch, result.Error);
    }

    [Fact]
    public void Only_an_unpublished_template_can_be_deleted()
    {
        var draftOnly = NewTemplate();
        var published = NewTemplate();
        published.Publish(Canonical(EmptyDefinition, '3'), "Published", FormPresentation.SinglePage, 0, UserId, Now);

        Assert.True(draftOnly.EnsureDeletable().IsSuccess);
        Assert.Equal(FormTemplateErrors.HasPublishedVersions, published.EnsureDeletable().Error);
    }

    private static FormTemplate NewTemplate() => Create(TenantId, UserId).Value;

    private static BuildingBlocks.Results.Result<FormTemplate> Create(Guid tenantId, Guid userId) =>
        FormTemplate.Create(
            tenantId,
            Name("Annual intake"),
            FormDescription.Create("Collect annual client details.").Value,
            ClientFormsTestDocuments.Definition(EmptyDefinition),
            Hash('0'),
            FormPurpose.ClientIntake,
            userId,
            Now
        );

    private static FormTemplateName Name(string value) => FormTemplateName.Create(value).Value;

    private static ContentHash Hash(char value) => ContentHash.Create(new string(value, 64)).Value;

    private static CanonicalDefinition Canonical(string json, char hash) => new(json, new string(hash, 64));
}
