using TaxVision.Notification.Domain.Emailing;
using TaxVision.Notification.Domain.Emailing.Templates;

namespace TaxVision.Notification.Tests.Domain;

public sealed class EmailTemplateTests
{
    [Fact]
    public void System_template_must_not_carry_tenant()
    {
        var result = EmailTemplate.Create(
            EmailScope.System,
            Guid.NewGuid(),
            "welcome",
            "Subject",
            null,
            null,
            "[]",
            null
        );

        Assert.True(result.IsFailure);
        Assert.Equal("EmailTemplate.Scope", result.Error.Code);
    }

    [Fact]
    public void Tenant_template_requires_tenant()
    {
        var result = EmailTemplate.Create(EmailScope.Tenant, null, "welcome", "Subject", null, null, "[]", null);

        Assert.True(result.IsFailure);
        Assert.Equal("EmailTemplate.Tenant", result.Error.Code);
    }

    [Fact]
    public void New_template_starts_as_draft()
    {
        var template = CreateTemplate();

        Assert.Equal(EmailTemplateStatus.Draft, template.Status);
        Assert.Null(template.CurrentVersionId);
    }

    [Fact]
    public void Publishing_activates_template_and_sets_current_version()
    {
        var template = CreateTemplate();
        var version = CreateVersion(template);

        var result = template.Publish(version, null);

        Assert.True(result.IsSuccess);
        Assert.Equal(EmailTemplateStatus.Active, template.Status);
        Assert.Equal(version.Id, template.CurrentVersionId);
        Assert.NotNull(template.PublishedAtUtc);
    }

    [Fact]
    public void Archived_template_cannot_be_published()
    {
        var template = CreateTemplate();
        template.Archive();

        var result = template.Publish(CreateVersion(template), null);

        Assert.True(result.IsFailure);
        Assert.Equal("EmailTemplate.Archived", result.Error.Code);
    }

    private static EmailTemplateVersion CreateVersion(EmailTemplate template) =>
        EmailTemplateVersion.Create(template.Id, 1, "Hi", "key", Guid.NewGuid(), null, null, null, null, null).Value;

    private static EmailTemplate CreateTemplate() =>
        EmailTemplate
            .Create(
                EmailScope.Tenant,
                Guid.NewGuid(),
                "welcome",
                "Hi {{ customer_name }}",
                null,
                null,
                "[\"customer_name\"]",
                null
            )
            .Value;
}
