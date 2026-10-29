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

    private EmailTemplateVersion BuildPushVersion(
        string subject,
        string text,
        IReadOnlyList<(
            string Name,
            VariableType Type,
            bool Required,
            string? DefaultValue,
            string? Description
        )>? variables = null
    )
    {
        var template = EmailTemplate
            .CreateNew(
                TemplateScope.System,
                null,
                TemplateKey.Create("test.push").Value,
                "Push test",
                null,
                Guid.NewGuid(),
                DateTime.UtcNow,
                TemplateChannel.Push
            )
            .Value;
        var textFileId = Guid.NewGuid();
        _storage.Seed(textFileId, text);
        return template.AddPushDraftVersion(subject, "scribe/text", textFileId, variables ?? [], DateTime.UtcNow).Value;
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
            TemplateChannel.Email,
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
            TemplateChannel.Email,
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
            TemplateChannel.Email,
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
            TemplateChannel.Email,
            null,
            _storage,
            _htmlSafetyValidator,
            default
        );

        Assert.False(result.Value.IsValid);
        Assert.Contains(result.Value.Errors, e => e.Code == "EmailHtmlSafety.Flexbox");
    }

    [Fact]
    public async Task ValidateAsync_rejects_static_push_lengths_urls_and_raw_filter()
    {
        var version = BuildPushVersion(
            new string('T', 65),
            $"{new string('B', 181)} https://example.com {{ value | raw }}"
        );

        var result = await TemplateVersionValidator.ValidateAsync(
            version,
            TemplateChannel.Push,
            null,
            _storage,
            _htmlSafetyValidator,
            default
        );

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsValid);
        Assert.Contains(result.Value.Errors, issue => issue.Code == "EmailTemplateVersion.PushBodyLength");
        Assert.Contains(result.Value.Errors, issue => issue.Code == "EmailTemplateVersion.PushUrl");
        Assert.Contains(result.Value.Errors, issue => issue.Code == "EmailTemplateVersion.PushRaw");
    }

    [Fact]
    public async Task ValidateAsync_rejects_a_push_branch_that_exceeds_the_limit_once_rendered()
    {
        var version = BuildPushVersion(
            $"{{% if language == 'Es' %}}{new string('T', 66)}{{% else %}}Short{{% endif %}}",
            "Body",
            [("language", VariableType.String, true, "En", null)]
        );

        var result = await ValidatePushAsync(version);

        Assert.Contains(result.Errors, issue => issue.Code == "EmailTemplateVersion.PushTitleLength");
    }

    [Fact]
    public async Task ValidateAsync_rejects_the_raw_filter_written_without_spaces()
    {
        var version = BuildPushVersion(
            "Title",
            "{{ language|raw }}",
            [("language", VariableType.String, true, "En", null)]
        );

        var result = await ValidatePushAsync(version);

        Assert.Contains(result.Errors, issue => issue.Code == "EmailTemplateVersion.PushRaw");
    }

    [Fact]
    public async Task ValidateAsync_rejects_push_output_whose_length_is_not_declared()
    {
        var version = BuildPushVersion("Title", "Hello {{ name }}", [("name", VariableType.String, true, null, null)]);

        var result = await ValidatePushAsync(version);

        Assert.Contains(result.Errors, issue => issue.Code == "EmailTemplateVersion.PushUnbounded");
    }

    [Theory]
    [InlineData(
        "{{ language | append: 'xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx' }}",
        "EmailTemplateVersion.PushTitleLength"
    )]
    [InlineData(
        "{{ language | default: 'xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx' }}",
        "EmailTemplateVersion.PushTitleLength"
    )]
    [InlineData("{{ language | replace: 'E', 'Español largo' }}", "EmailTemplateVersion.PushUnbounded")]
    public async Task ValidateAsync_counts_or_rejects_push_filters(string subject, string expectedError)
    {
        var version = BuildPushVersion(subject, "Body", [("language", VariableType.String, true, "En", null)]);

        var result = await ValidatePushAsync(version);

        Assert.Contains(result.Errors, issue => issue.Code == expectedError);
    }

    [Fact]
    public async Task ValidateAsync_accepts_a_truncated_output_even_without_a_declared_length()
    {
        var version = BuildPushVersion(
            "Hello {{ name | upcase | truncate: 20 }}",
            "Body",
            [("name", VariableType.String, true, null, null)]
        );

        var result = await ValidatePushAsync(version);

        Assert.True(result.IsValid, string.Join(", ", result.Errors.Select(e => e.Code)));
    }

    [Fact]
    public async Task ValidateAsync_accepts_every_seeded_push_template()
    {
        foreach (var seed in TaxVision.Scribe.Application.Templates.Seed.NotificationTemplateSeedSource.All)
        {
            if (seed.Channel != TemplateChannel.Push)
                continue;
            var version = BuildPushVersion(seed.Subject, seed.Text!, seed.Variables);

            var result = await ValidatePushAsync(version);

            Assert.True(result.IsValid, $"{seed.TemplateKey}: {string.Join(", ", result.Errors.Select(e => e.Code))}");
        }
    }

    private async Task<TemplateValidationOutcome> ValidatePushAsync(EmailTemplateVersion version)
    {
        var result = await TemplateVersionValidator.ValidateAsync(
            version,
            TemplateChannel.Push,
            null,
            _storage,
            _htmlSafetyValidator,
            default
        );
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    [Fact]
    public void Push_draft_allows_liquid_source_longer_than_the_rendered_title_limit()
    {
        var version = BuildPushVersion("{% if language == 'Es' %}Formulario{% else %}Form{% endif %}", "Body");

        Assert.Contains("{% if", version.Subject, StringComparison.Ordinal);
    }
}
