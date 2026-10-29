using System.Text.RegularExpressions;
using Fluid;
using TaxVision.Scribe.Application.Templates.Seed;
using TaxVision.Scribe.Application.Templates.Validation;
using TaxVision.Scribe.Domain.Templates;

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
    public void Client_form_push_seeds_are_text_only_safe_and_render_within_limits()
    {
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            "task.form_request_assigned.push.v1",
            "task.form_request_returned.push.v1",
            "task.form_request_reminder.push.v1",
            "task.form_attachment_rejected.push.v1",
            "task.form_request_completed.push.v1",
            "task.form_request_submitted.push.v1",
            "task.form_action_failed.push.v1",
        };
        var seeds = NotificationTemplateSeedSource.All.Where(seed => seed.Channel == TemplateChannel.Push).ToArray();

        Assert.True(expected.SetEquals(seeds.Select(seed => seed.EventKey)));
        foreach (var seed in seeds)
        {
            Assert.Equal(string.Empty, seed.Html);
            Assert.False(string.IsNullOrWhiteSpace(seed.Text));
            Assert.DoesNotContain("| raw", seed.Subject + seed.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotMatch(@"https?://|www\.", seed.Subject + seed.Text);
            Assert.DoesNotContain(
                seed.Variables,
                variable => variable.Name is "form_title" or "customer_name" or "email" or "token" or "message"
            );
            AssertRenderedLimits(seed, "En", isOverdue: false);
            AssertRenderedLimits(seed, "Es", isOverdue: true);
        }
    }

    private static void AssertRenderedLimits(NotificationTemplateSeed seed, string language, bool isOverdue)
    {
        var context = new TemplateContext();
        context.SetValue("language", language);
        context.SetValue("is_overdue", isOverdue);
        var subject = Parser.Parse(seed.Subject).Render(context);
        var text = Parser.Parse(seed.Text!).Render(context);

        Assert.InRange(subject.Length, 1, 65);
        Assert.InRange(text.Length, 1, 180);
    }

    [Theory]
    [InlineData("task.form_access_link.v1", "Formulario seguro", "Abrir formulario seguro")]
    [InlineData("task.form_access_code.v1", "código de verificación", "Verifica tu identidad")]
    [InlineData("task.form_request_assigned.v1", "Acción requerida", "Abrir formulario")]
    [InlineData("task.form_request_returned.v1", "Cambios solicitados", "Inicia sesión")]
    [InlineData("task.form_request_completed.v1", "Recibido", "como completado")]
    [InlineData("task.form_attachment_rejected.v1", "No pudimos aceptar", "análisis de seguridad")]
    [InlineData("task.form_request_reminder.v1", "Recordatorio", "está vencido")]
    [InlineData("task.form_participant_returned.v1", "Cambios solicitados", "pide a tu oficina")]
    [InlineData("task.form_participant_completed.v1", "Recibido", "No necesitas hacer nada")]
    [InlineData("task.form_participant_cancelled.v1", "Cancelado", "canceló")]
    [InlineData("task.form_participant_attachment_rejected.v1", "No pudimos aceptar", "análisis de seguridad")]
    [InlineData("task.form_participant_reminder.v1", "Recordatorio", "está vencido")]
    public void Client_form_emails_for_clients_and_externals_render_in_spanish(
        string templateKey,
        string spanishSubject,
        string spanishBody
    )
    {
        var seed = Assert.Single(NotificationTemplateSeedSource.All, s => s.TemplateKey == templateKey);

        var spanish = Render(seed, "Es");
        var english = Render(seed, "En");

        Assert.Contains(seed.Variables, variable => variable.Name == "language");
        Assert.Contains(spanishSubject, spanish.Subject, StringComparison.Ordinal);
        Assert.Contains(spanishBody, spanish.Html, StringComparison.Ordinal);
        Assert.DoesNotContain(spanishBody, english.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("Hi ", spanish.Html, StringComparison.Ordinal);
        var preheader = NotificationTemplateSeedSource
            .VariablesWithPreheader(seed)
            .Single(variable => variable.Name == "preheader")
            .DefaultValue!;
        Assert.NotEqual(RenderText(preheader, "Es"), RenderText(preheader, "En"));
        // Una plantilla ya sembrada que cambia sube su ContentVersion para que el seeder la republique.
        Assert.True(seed.ContentVersion > 21 || templateKey == "task.form_participant_reminder.v1");
    }

    [Fact]
    public void Spanish_client_form_texts_carry_their_accents()
    {
        var unaccented = new[]
        {
            "envio ",
            "solicito ",
            "marco ",
            "sesion ",
            "revision",
            "Accion",
            "atencion",
            "esta vencido",
        };
        var seeds = NotificationTemplateSeedSource.All.Where(seed =>
            seed.TemplateKey.StartsWith("task.form_", StringComparison.Ordinal)
        );

        foreach (var seed in seeds)
        {
            var spanish = Render(seed, "Es");
            var content = $"{spanish.Subject} {spanish.Html} {spanish.Text}";
            foreach (var word in unaccented)
                Assert.DoesNotContain(word, content, StringComparison.Ordinal);
        }
    }

    private static string RenderText(string source, string language)
    {
        var context = new TemplateContext();
        context.SetValue("language", language);
        return Parser.Parse(source).Render(context, NullEncoder.Default);
    }

    private static (string Subject, string Html, string Text) Render(NotificationTemplateSeed seed, string language)
    {
        var context = new TemplateContext();
        context.SetValue("language", language);
        context.SetValue("is_overdue", true);
        context.SetValue("customer_name", "Ana");
        context.SetValue("participant_name", "Ana");
        context.SetValue("request_title", "Organizer");
        context.SetValue("form_title", "Organizer");
        context.SetValue("comment_count", 2);
        context.SetValue("due_date", "2026-11-01");
        context.SetValue("has_message", true);
        context.SetValue("file_extension", "pdf");
        context.SetValue("client_message", "The file couldn't be accepted because our security scan flagged it.");
        context.SetValue(
            "client_message_es",
            "No pudimos aceptar el archivo porque nuestro análisis de seguridad lo marcó."
        );
        context.SetValue("code", "123456");
        context.SetValue("expires_at", "2026-11-01 10:00");
        return (
            Parser.Parse(seed.Subject).Render(context, NullEncoder.Default),
            Parser.Parse(seed.Html).Render(context, NullEncoder.Default),
            seed.Text is null ? string.Empty : Parser.Parse(seed.Text).Render(context, NullEncoder.Default)
        );
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
