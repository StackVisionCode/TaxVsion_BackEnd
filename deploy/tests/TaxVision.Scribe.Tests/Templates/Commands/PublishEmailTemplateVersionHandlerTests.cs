using TaxVision.Scribe.Application.Templates.Commands;
using TaxVision.Scribe.Application.Templates.Validation;
using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.Templates;
using TaxVision.Scribe.Domain.ValueObjects;
using TaxVision.Scribe.Tests.Rendering;
using TaxVision.Scribe.Tests.Templates.Validation;

namespace TaxVision.Scribe.Tests.Templates.Commands;

public sealed class PublishEmailTemplateVersionHandlerTests
{
    private readonly FakeTemplateStorageService _storage = new();
    private readonly EmailHtmlSafetyValidator _htmlSafetyValidator = new();

    private (EmailTemplate Template, EmailTemplateVersion Version) BuildDraft(
        string html,
        IReadOnlyList<(
            string Name,
            VariableType Type,
            bool Required,
            string? DefaultValue,
            string? Description
        )> variables
    )
    {
        var template = EmailTemplate
            .CreateNew(
                TemplateScope.System,
                null,
                TemplateKey.Create("test.template").Value,
                "Test",
                null,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;

        var htmlFileId = Guid.NewGuid();
        _storage.Seed(htmlFileId, html);

        var version = template
            .AddDraftVersion(
                "Subject",
                "scribe/html",
                htmlFileId,
                null,
                null,
                null,
                null,
                null,
                null,
                Guid.NewGuid(),
                1,
                variables,
                DateTime.UtcNow
            )
            .Value;

        return (template, version);
    }

    [Fact]
    public async Task Handle_rejects_publish_when_the_html_uses_an_undeclared_variable()
    {
        var (template, version) = BuildDraft(
            "<p>{{ name }} {{ surprise }}</p>",
            [("name", VariableType.String, true, null, null)]
        );
        var repository = new FakeEmailTemplateRepository(template);
        var command = new PublishEmailTemplateVersionCommand(template.Id, version.Id, null, true, Guid.NewGuid());

        var result = await PublishEmailTemplateVersionHandler.Handle(
            command,
            repository,
            _storage,
            _htmlSafetyValidator,
            new FakeUnitOfWork(),
            default
        );

        Assert.True(result.IsFailure);
        Assert.Equal("EmailTemplateVersion.ValidationFailed", result.Error.Code);
        Assert.Equal(EmailVersionStatus.Draft, version.Status);
    }

    [Fact]
    public async Task Handle_publishes_when_all_placeholders_are_declared_and_html_is_safe()
    {
        var (template, version) = BuildDraft("<p>{{ name }}</p>", [("name", VariableType.String, true, null, null)]);
        var repository = new FakeEmailTemplateRepository(template);
        var command = new PublishEmailTemplateVersionCommand(template.Id, version.Id, null, true, Guid.NewGuid());

        var result = await PublishEmailTemplateVersionHandler.Handle(
            command,
            repository,
            _storage,
            _htmlSafetyValidator,
            new FakeUnitOfWork(),
            default
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(EmailVersionStatus.Published, version.Status);
    }

    [Fact]
    public async Task Handle_rejects_publish_when_the_caller_is_not_a_platform_admin_for_a_system_template()
    {
        var (template, version) = BuildDraft("<p>{{ name }}</p>", [("name", VariableType.String, true, null, null)]);
        var repository = new FakeEmailTemplateRepository(template);
        var command = new PublishEmailTemplateVersionCommand(template.Id, version.Id, null, false, Guid.NewGuid());

        var result = await PublishEmailTemplateVersionHandler.Handle(
            command,
            repository,
            _storage,
            _htmlSafetyValidator,
            new FakeUnitOfWork(),
            default
        );

        Assert.True(result.IsFailure);
        Assert.Equal("EmailTemplate.Forbidden", result.Error.Code);
    }
}
