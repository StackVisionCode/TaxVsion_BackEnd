using TaxVision.Scribe.Application.Templates.Validation;
using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.Templates;
using TaxVision.Scribe.Domain.ValueObjects;

namespace TaxVision.Scribe.Tests.Templates.Validation;

public sealed class TemplateVersionValidatorTests
{
    private readonly FakeTemplateStorageService _storage = new();
    private readonly EmailHtmlSafetyValidator _htmlSafetyValidator = new();

    private EmailTemplateVersion BuildVersion(
        string subject,
        string html,
        string? text,
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
        Guid? textFileId = null;
        if (text is not null)
        {
            textFileId = Guid.NewGuid();
            _storage.Seed(textFileId.Value, text);
        }

        return template
            .AddDraftVersion(
                subject,
                "scribe/html",
                htmlFileId,
                text is null ? null : "scribe/text",
                textFileId,
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
    }

    [Fact]
    public async Task ValidateAsync_passes_when_all_placeholders_are_declared()
    {
        var version = BuildVersion(
            "Hi {{ name }}",
            "<p>Hello {{ name }}, {% if premium %}VIP{% endif %}</p>",
            "Hello {{ name }}",
            [("name", VariableType.String, true, null, null), ("premium", VariableType.Bool, false, null, null)]
        );

        var result = await TemplateVersionValidator.ValidateAsync(
            version,
            null,
            _storage,
            _htmlSafetyValidator,
            default
        );

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsValid);
        Assert.Empty(result.Value.Errors);
    }

    [Fact]
    public async Task ValidateAsync_reports_an_undeclared_variable_used_in_the_html()
    {
        var version = BuildVersion(
            "Hi {{ name }}",
            "<p>Hello {{ name }}, click {{ link }}</p>",
            null,
            [("name", VariableType.String, true, null, null)]
        );

        var result = await TemplateVersionValidator.ValidateAsync(
            version,
            null,
            _storage,
            _htmlSafetyValidator,
            default
        );

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsValid);
        Assert.Contains(
            result.Value.Errors,
            e => e.Code == "EmailTemplateVersion.UndeclaredVariable" && e.Message.Contains("link")
        );
    }

    [Fact]
    public async Task ValidateAsync_reports_an_undeclared_variable_used_in_a_conditional()
    {
        var version = BuildVersion(
            "Hi {{ name }}",
            "<p>{% if vip %}VIP{% endif %}</p>",
            null,
            [("name", VariableType.String, true, null, null)]
        );

        var result = await TemplateVersionValidator.ValidateAsync(
            version,
            null,
            _storage,
            _htmlSafetyValidator,
            default
        );

        Assert.False(result.Value.IsValid);
        Assert.Contains(result.Value.Errors, e => e.Message.Contains("vip"));
    }

    [Fact]
    public async Task ValidateAsync_reports_html_safety_issues_alongside_variable_issues()
    {
        var version = BuildVersion(
            "Hi {{ name }}",
            "<div style=\"display:flex;\">{{ name }}</div>",
            null,
            [("name", VariableType.String, true, null, null)]
        );

        var result = await TemplateVersionValidator.ValidateAsync(
            version,
            null,
            _storage,
            _htmlSafetyValidator,
            default
        );

        Assert.False(result.Value.IsValid);
        Assert.Contains(result.Value.Errors, e => e.Code == "EmailHtmlSafety.Flexbox");
    }
}
