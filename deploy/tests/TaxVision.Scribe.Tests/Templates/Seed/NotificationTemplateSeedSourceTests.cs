using Fluid;
using TaxVision.Scribe.Application.Templates.Seed;
using TaxVision.Scribe.Application.Templates.Validation;

namespace TaxVision.Scribe.Tests.Templates.Seed;

/// <summary>
/// Recorre los 23 seeds: cada Html/Subject debe parsear como Fluid (caza typos en {% if %}/{{ }}),
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
}
