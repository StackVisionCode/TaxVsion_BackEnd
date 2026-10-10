using Fluid;
using TaxVision.Scribe.Application.Templates.Seed;
using TaxVision.Scribe.Application.Templates.Validation;

namespace TaxVision.Scribe.Tests.Templates.Seed;

/// <summary>
/// Recorre los 27 seeds: cada Html/Subject debe parsear como Fluid (caza typos en {% if %}/{{ }}),
/// el Html debe pasar el preflight de seguridad, y ninguno debe seguir mencionando la marca vieja.
/// </summary>
public sealed class NotificationTemplateSeedSourceTests
{
    private static readonly FluidParser Parser = new();
    private static readonly EmailHtmlSafetyValidator Validator = new();

    [Fact]
    public void All_seed_html_and_subjects_parse_as_fluid_and_pass_safety()
    {
        foreach (var seed in NotificationTemplateSeedSource.All)
        {
            Assert.True(Parser.TryParse(seed.Html, out _, out var htmlError), $"{seed.TemplateKey} HTML: {htmlError}");
            Assert.True(
                Parser.TryParse(seed.Subject, out _, out var subjectError),
                $"{seed.TemplateKey} subject: {subjectError}"
            );

            var outcome = Validator.Validate(seed.Html);
            Assert.True(
                outcome.IsAcceptable,
                $"{seed.TemplateKey}: {string.Join(", ", outcome.Errors.Select(e => e.Message))}"
            );
        }
    }

    [Fact]
    public void No_seed_still_mentions_the_old_brand()
    {
        foreach (var seed in NotificationTemplateSeedSource.All)
        {
            Assert.DoesNotContain("TaxVision", seed.Html);
            Assert.DoesNotContain("TaxVision", seed.Subject);
        }
    }

    [Fact]
    public void Meeting_invitation_template_is_seeded_with_its_keys_and_a_preheader()
    {
        var seed = Assert.Single(
            NotificationTemplateSeedSource.All,
            s => s.EventKey == "communication.meeting.invitation_created.v1"
        );
        Assert.Equal("communication.meeting.invitation", seed.TemplateKey);
        // El join_link es la variable que lleva al subdominio correcto del tenant (bug prod #1).
        Assert.Contains(seed.Variables, v => v.Name == "join_link");

        var withPreheader = NotificationTemplateSeedSource.VariablesWithPreheader(seed);
        Assert.Contains(withPreheader, v => v.Name == "preheader");
    }

    [Fact]
    public void Partial_copy_subject_does_not_expose_internal_progress_state()
    {
        var seed = Assert.Single(
            NotificationTemplateSeedSource.All,
            item => item.EventKey == "sig.partial_copy_ready.v1"
        );

        Assert.DoesNotContain("in progress", seed.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("en progreso", seed.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.True(seed.ContentVersion >= 16);
    }

    [Fact]
    public void Certificate_template_can_identify_a_per_document_certificate()
    {
        var seed = Assert.Single(
            NotificationTemplateSeedSource.All,
            item => item.EventKey == "sig.certificate_ready.v1"
        );

        Assert.Contains(seed.Variables, variable => variable.Name == "document_title" && !variable.Required);
        Assert.Contains("document_title", seed.Subject);
        Assert.Contains("document_title", seed.Html);
        Assert.True(seed.ContentVersion >= 17);
    }
}
