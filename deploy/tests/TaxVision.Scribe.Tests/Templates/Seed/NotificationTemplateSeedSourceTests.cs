using System.Text.RegularExpressions;
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

    // ---------- Cobro automático fallido: un correo por intento, con dos variantes ----------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failed_charge_template_renders_the_retry_and_the_final_variants(bool isFinal)
    {
        var seed = FailedChargeSeed();
        var context = new TemplateContext();
        foreach (var (name, value) in FailedChargeVariables(isFinal))
            context.SetValue(name, value);

        var subject = Parser.Parse(seed.Subject).Render(context);
        var html = Parser.Parse(seed.Html).Render(context);

        Assert.Contains("Go to Manage subscription", html);
        Assert.Contains("https://www.taxoffix.io/account/plan", html);
        Assert.Contains("Visa ending in 4242", html);
        Assert.Contains("Professional plan", html);
        Assert.Contains("Your bank declined the charge.", html);
        if (isFinal)
        {
            Assert.StartsWith("Final notice:", subject);
            Assert.Contains("As a result,", html);
            Assert.Contains("your subscription has been suspended", html);
            Assert.DoesNotContain("Next attempt", html);
        }
        else
        {
            Assert.StartsWith("Action needed:", subject);
            Assert.Contains("Next attempt", html);
            Assert.Contains("October 23, 2026", html);
            Assert.Contains("2 of 4", html);
            Assert.DoesNotContain("As a result,", html);
        }
    }

    // Sin tarjeta conocida (p. ej. no había ninguna guardada) no se inventan "card ending in".
    [Fact]
    public void Failed_charge_template_without_a_known_card_does_not_mention_one()
    {
        var context = new TemplateContext();
        foreach (var (name, value) in FailedChargeVariables(isFinal: true))
            context.SetValue(name, name == "has_card" ? false : value);

        var html = Parser.Parse(FailedChargeSeed().Html).Render(context);

        Assert.DoesNotContain("ending in", html);
        Assert.Contains("your account", html);
    }

    // Toda variable que usa la plantilla tiene que estar declarada (Scribe rechaza las no declaradas al publicar).
    [Fact]
    public void Failed_charge_template_declares_every_variable_it_uses_and_was_republished()
    {
        var seed = FailedChargeSeed();
        var declared = seed.Variables.Select(v => v.Name).ToHashSet();
        var used = Regex
            .Matches(seed.Html + seed.Subject, @"(?:\{\{\s*|\{%\s*(?:if|unless)\s+)([a-z_][a-z0-9_]*)")
            .Select(m => m.Groups[1].Value)
            .ToHashSet();

        Assert.Empty(used.Except(declared));
        // Cambió el contenido: sin subir la versión el seeder no republica y seguiría saliendo el correo viejo.
        Assert.True(seed.ContentVersion >= 18);
    }

    private static NotificationTemplateSeed FailedChargeSeed() =>
        Assert.Single(
            NotificationTemplateSeedSource.All,
            item => item.EventKey == "subscription.recurring_charge_failed.v1"
        );

    private static IEnumerable<(string Name, object Value)> FailedChargeVariables(bool isFinal) =>
        [
            ("first_name", "Carlos"),
            ("item_description", "Professional plan"),
            ("amount", "$49.00"),
            ("has_card", true),
            ("card_brand", "Visa"),
            ("card_last4", "4242"),
            ("attempt_number", isFinal ? "4" : "2"),
            ("max_attempts", "4"),
            ("is_final", isFinal),
            ("next_retry_date", isFinal ? "" : "October 23, 2026"),
            ("friendly_reason", "Your bank declined the charge."),
            ("suspension_effect", "your subscription has been suspended and your team can no longer sign in"),
            ("manage_url", "https://www.taxoffix.io/account/plan"),
            ("product_name", "TaxOffix"),
        ];

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
