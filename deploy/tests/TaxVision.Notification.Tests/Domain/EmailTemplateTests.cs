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
        var versionId = Guid.NewGuid();

        var result = template.MarkPublished(versionId);

        Assert.True(result.IsSuccess);
        Assert.Equal(EmailTemplateStatus.Active, template.Status);
        Assert.Equal(versionId, template.CurrentVersionId);
        Assert.NotNull(template.PublishedAtUtc);
    }

    [Fact]
    public void Archived_template_cannot_be_published()
    {
        var template = CreateTemplate();
        template.Archive();

        var result = template.MarkPublished(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("EmailTemplate.Archived", result.Error.Code);
    }

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
