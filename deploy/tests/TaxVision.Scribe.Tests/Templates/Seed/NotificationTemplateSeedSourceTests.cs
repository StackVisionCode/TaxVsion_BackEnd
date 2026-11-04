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

    // ---------- Reuniones (F6): invitación v2, cancelación, reprogramación y recordatorio ----------

    [Theory]
    [InlineData("communication.meeting.invitation_created.v1", "communication.meeting.invitation", 21)]
    [InlineData("communication.meeting.cancelled.v1", "communication.meeting.cancelled.v1", 1)]
    [InlineData("communication.meeting.rescheduled.v1", "communication.meeting.rescheduled.v1", 1)]
    [InlineData("communication.meeting.reminder.v1", "communication.meeting.reminder.v1", 1)]
    public void Meeting_templates_declare_every_variable_they_use_and_carry_their_version(
        string eventKey,
        string templateKey,
        int minVersion
    )
    {
        var seed = Assert.Single(NotificationTemplateSeedSource.All, s => s.EventKey == eventKey);
        Assert.Equal(templateKey, seed.TemplateKey);

        var declared = seed.Variables.Select(v => v.Name).ToHashSet();
        var used = Regex
            .Matches(seed.Html + seed.Subject, @"(?:\{\{\s*|\{%\s*(?:if|unless|elsif)\s+)([a-z_][a-z0-9_]*)")
            .Select(m => m.Groups[1].Value)
            .ToHashSet();
        Assert.Empty(used.Except(declared));
        Assert.Contains("meeting_title", declared);
        Assert.True(
            seed.ContentVersion >= minVersion,
            $"{templateKey}: ContentVersion {seed.ContentVersion} < {minVersion}"
        );
        Assert.Contains(NotificationTemplateSeedSource.VariablesWithPreheader(seed), v => v.Name == "preheader");
        Assert.Equal("system-base", seed.LayoutKey);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Meeting_invitation_renders_the_scheduled_and_the_in_progress_branches(bool inProgress)
    {
        var seed = Assert.Single(
            NotificationTemplateSeedSource.All,
            s => s.EventKey == "communication.meeting.invitation_created.v1"
        );
        var context = new TemplateContext();
        context.SetValue("invitee_name", "Cliente");
        context.SetValue("join_link", "https://manfer.taxoffix.io/meet/abc");
        context.SetValue("expires_at", "2026-11-06 12:00");
        context.SetValue("meeting_title", "Revisión de la 1040");
        // Notification manda null y el renderer lo convierte en "" (FluidTemplateRenderer): se prueba lo que llega.
        context.SetValue("start_local", inProgress ? string.Empty : "2026-11-05 14:00");
        context.SetValue("time_zone", "America/Santo_Domingo");
        context.SetValue("duration_minutes", 45);
        context.SetValue("host_name", "María Pérez");
        context.SetValue("meeting_code", "ABC-123");
        context.SetValue("in_progress", inProgress);
        context.SetValue("product_name", "TaxOffix");

        var subject = Parser.Parse(seed.Subject).Render(context);
        var html = Parser.Parse(seed.Html).Render(context);

        Assert.Contains("Revisión de la 1040", html);
        Assert.Contains("ABC-123", html);
        Assert.Contains("María Pérez", html);
        if (inProgress)
        {
            Assert.StartsWith("Join now:", subject);
            Assert.Contains("happening right now", html);
            Assert.DoesNotContain("When:", html);
            Assert.DoesNotContain("calendar invitation is attached", html);
        }
        else
        {
            Assert.StartsWith("Invitation:", subject);
            Assert.Contains("2026-11-05 14:00 (America/Santo_Domingo)", html);
            Assert.Contains("45 min", html);
            Assert.Contains("calendar invitation is attached", html);
            Assert.DoesNotContain("happening right now", html);
        }
    }

    /// <summary>
    /// Fluid trata "" (y es lo que el renderer manda por un null) como verdadero: un <c>{% if x %}</c> a
    /// secas pintaría "When:" vacío. Las filas opcionales comparan con <c>blank</c>, que cubre "" y nil.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Meeting_optional_rows_are_hidden_for_null_and_empty_values(string? missing)
    {
        var seed = Assert.Single(
            NotificationTemplateSeedSource.All,
            s => s.EventKey == "communication.meeting.reminder.v1"
        );
        var context = new TemplateContext();
        context.SetValue("meeting_title", "Revisión de la 1040");
        context.SetValue("minutes_before", 15);
        context.SetValue("start_local", missing);
        context.SetValue("host_name", missing);
        context.SetValue("meeting_code", missing);
        context.SetValue("duration_minutes", missing);
        context.SetValue("product_name", "TaxOffix");

        var html = Parser.Parse(seed.Html).Render(context);

        Assert.DoesNotContain("When:", html);
        Assert.DoesNotContain("Host:", html);
        Assert.DoesNotContain("Meeting code:", html);
        Assert.DoesNotContain("enter the meeting code", html);
    }

    [Fact]
    public void Meeting_rescheduled_template_shows_the_previous_and_the_new_time()
    {
        var seed = Assert.Single(
            NotificationTemplateSeedSource.All,
            s => s.EventKey == "communication.meeting.rescheduled.v1"
        );
        var context = new TemplateContext();
        context.SetValue("meeting_title", "Revisión de la 1040");
        context.SetValue("previous_local", "2026-11-05 14:00");
        context.SetValue("new_local", "2026-11-05 16:00");
        context.SetValue("time_zone", "America/Santo_Domingo");
        context.SetValue("product_name", "TaxOffix");

        var html = Parser.Parse(seed.Html).Render(context);

        Assert.Contains("Previously:", html);
        Assert.Contains("2026-11-05 14:00", html);
        Assert.Contains("New time:", html);
        Assert.Contains("2026-11-05 16:00", html);
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

    // ---------- F4 Cumplimiento: revisión anual del WISP ----------

    [Fact]
    public void Wisp_review_template_declares_every_variable_it_uses_and_carries_version_23()
    {
        var seed = WispSeed();
        var declared = seed.Variables.Select(v => v.Name).ToHashSet();
        var used = Regex
            .Matches(seed.Html + seed.Subject, @"(?:\{\{\s*|\{%\s*(?:if|unless|elsif)\s+)([a-z_][a-z0-9_]*)")
            .Select(m => m.Groups[1].Value)
            .ToHashSet();

        Assert.Empty(used.Except(declared));
        Assert.Equal("tenant.wisp_review_due.v1", seed.TemplateKey);
        Assert.Equal("system-base", seed.LayoutKey);
        Assert.Equal(TemplateChannel.Email, seed.Channel);
        // Versión propia por encima del default (22): sin eso el seeder no la publicaría sobre una guardada.
        Assert.True(seed.ContentVersion >= 23, $"ContentVersion {seed.ContentVersion} < 23");
        Assert.Contains(NotificationTemplateSeedSource.VariablesWithPreheader(seed), v => v.Name == "preheader");
    }

    [Theory]
    [InlineData(false, "Your security plan review is due November 30, 2026", "is due on")]
    [InlineData(true, "Your security plan review is overdue", "was due on")]
    public void Wisp_review_template_renders_both_stages(bool isOverdue, string subject, string bodyFragment)
    {
        var seed = WispSeed();
        var context = new TemplateContext();
        context.SetValue("is_overdue", isOverdue);
        context.SetValue("due_date", "November 30, 2026");
        context.SetValue("settings_url", "https://manfer.taxoffix.io/company/settings#compliance");
        context.SetValue("product_name", "TaxOffix");

        var renderedSubject = Parser.Parse(seed.Subject).Render(context);
        var html = Parser.Parse(seed.Html).Render(context);

        Assert.Equal(subject, renderedSubject);
        Assert.Contains(bodyFragment, html);
        Assert.Contains("<strong style=\"color:#23384B;\">November 30, 2026</strong>", html);
        Assert.Contains("FTC Safeguards Rule", html);
        Assert.Contains("at least once a year", html);
        Assert.Contains("Office settings &rarr; Compliance", html);
        Assert.Contains("href=\"https://manfer.taxoffix.io/company/settings#compliance\"", html);
    }

    // ---------- Avisos del retiro de empleados (offboard) ----------

    public static TheoryData<string> OffboardingNoticeKeys =>
        new()
        {
            "tenant.security_coordinator_vacated.v1",
            "campaign.schedules_paused_on_offboarding.v1",
            "sig.preparer_attention_required.v1",
            "sig.preparer_changed.v1",
            "storage.cloud_connection_review_required.v1",
            "connectors.office_mailbox_review_required.v1",
        };

    [Theory]
    [MemberData(nameof(OffboardingNoticeKeys))]
    public void Offboarding_templates_declare_every_variable_and_carry_their_own_version(string key)
    {
        var seed = Seed(key);
        var declared = seed.Variables.Select(v => v.Name).ToHashSet();
        var used = Regex
            .Matches(seed.Html + seed.Subject, @"(?:\{\{\s*|\{%\s*(?:if|unless|elsif)\s+)([a-z_][a-z0-9_]*)")
            .Select(m => m.Groups[1].Value)
            .ToHashSet();

        Assert.Empty(used.Except(declared));
        Assert.Equal(key, seed.TemplateKey);
        Assert.Equal("system-base", seed.LayoutKey);
        Assert.Equal(TemplateChannel.Email, seed.Channel);
        // Versión explícita por encima del default (22) y del WISP (23): sin eso el seeder no la publica.
        Assert.Equal(24, seed.ContentVersion);
        Assert.Contains(NotificationTemplateSeedSource.VariablesWithPreheader(seed), v => v.Name == "preheader");
        // Copy para la oficina: sin "tenant" ni códigos de permisos.
        Assert.DoesNotContain("tenant", seed.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".manage", seed.Html);
    }

    [Theory]
    [InlineData(1, "A scheduled campaign was paused", "we paused it:")]
    [InlineData(2, "2 scheduled campaigns were paused", "we paused them:")]
    public void Campaign_pause_template_lists_the_campaigns(int count, string subject, string fragment)
    {
        var seed = Seed("campaign.schedules_paused_on_offboarding.v1");
        var context = new TemplateContext();
        context.SetValue("campaign_count", count);
        context.SetValue(
            "campaign_names",
            count == 1 ? new[] { "Spring promo" } : new[] { "Spring promo", "Tax tips" }
        );
        context.SetValue("campaigns_url", "https://manfer.taxoffix.io/campaigns");
        context.SetValue("product_name", "TaxOffix");

        Assert.Equal(subject, Parser.Parse(seed.Subject).Render(context));
        var html = Parser.Parse(seed.Html).Render(context);
        Assert.Contains(fragment, html);
        Assert.Contains(count == 1 ? "Spring promo<" : "Spring promo, Tax tips<", html);
        Assert.Contains("href=\"https://manfer.taxoffix.io/campaigns\"", html);
    }

    [Theory]
    [InlineData(
        "PreparerCleared",
        "Choose a new preparer for “Smith 1040”",
        "Choose a new preparer before you send it."
    )]
    [InlineData(
        "PreparerChangeBlocked",
        "“Smith 1040” needs a new preparer",
        "Change the preparer so the return can be signed."
    )]
    [InlineData("UnsentRequestKept", "Review the unsent request “Smith 1040”", "hasn't been sent")]
    [InlineData("Other", "“Smith 1040” needs your attention", "Open it to see what needs to change.")]
    public void Preparer_attention_template_follows_the_reason(string reason, string subject, string fragment)
    {
        var seed = Seed("sig.preparer_attention_required.v1");
        var context = new TemplateContext();
        context.SetValue("reason", reason);
        context.SetValue("request_title", "Smith 1040");
        context.SetValue("request_url", "https://manfer.taxoffix.io/signature/requests/1");
        context.SetValue("product_name", "TaxOffix");

        Assert.Equal(subject, Parser.Parse(seed.Subject).Render(context));
        Assert.Contains(fragment, Parser.Parse(seed.Html).Render(context));
    }

    [Theory]
    [InlineData(true, "You're now the preparer for “Smith 1040”", "You'll sign this return as its preparer.")]
    [InlineData(
        false,
        "New preparer for “Smith 1040”",
        "<strong style=\"color:#23384B;\">Dana Preparer</strong> is now the preparer"
    )]
    public void Preparer_changed_template_speaks_to_the_preparer_or_the_author(
        bool isNewPreparer,
        string subject,
        string fragment
    )
    {
        var seed = Seed("sig.preparer_changed.v1");
        var context = new TemplateContext();
        context.SetValue("is_new_preparer", isNewPreparer);
        context.SetValue("is_offboarding", true);
        context.SetValue("request_title", "Smith 1040");
        context.SetValue("preparer_name", "Dana Preparer");
        context.SetValue("request_url", "https://manfer.taxoffix.io/signature/requests/1");
        context.SetValue("product_name", "TaxOffix");

        Assert.Equal(subject, Parser.Parse(seed.Subject).Render(context));
        var html = Parser.Parse(seed.Html).Render(context);
        Assert.Contains("is no longer with your office.", html);
        Assert.Contains(fragment, html);
    }

    [Fact]
    public void Cloud_and_mailbox_templates_say_why_and_link_to_the_fix()
    {
        var cloud = new TemplateContext();
        cloud.SetValue("provider_name", "Google Drive");
        cloud.SetValue("account_email", "docs@office.test");
        cloud.SetValue("reason", "AccountOfOffboardedUser");
        cloud.SetValue("storage_url", "https://manfer.taxoffix.io/storage");
        var cloudSeed = Seed("storage.cloud_connection_review_required.v1");
        Assert.Equal("Reconnect your office's Google Drive connection", Parser.Parse(cloudSeed.Subject).Render(cloud));
        var cloudHtml = Parser.Parse(cloudSeed.Html).Render(cloud);
        Assert.Contains("which belongs to someone who's no longer with your office", cloudHtml);
        Assert.Contains("href=\"https://manfer.taxoffix.io/storage\"", cloudHtml);

        var mailbox = new TemplateContext();
        mailbox.SetValue("mailbox_email", "front@office.test");
        mailbox.SetValue("reason", "ConnectedByOffboardedUser");
        mailbox.SetValue("mailbox_url", "https://manfer.taxoffix.io/email?accountId=1");
        var mailboxSeed = Seed("connectors.office_mailbox_review_required.v1");
        Assert.Equal(
            "Reconnect the office mailbox front@office.test",
            Parser.Parse(mailboxSeed.Subject).Render(mailbox)
        );
        var mailboxHtml = Parser.Parse(mailboxSeed.Html).Render(mailbox);
        Assert.Contains("was connected by someone who's no longer with your office", mailboxHtml);
        Assert.Contains("href=\"https://manfer.taxoffix.io/email?accountId=1\"", mailboxHtml);
    }

    [Fact]
    public void Offboarding_incomplete_template_lists_the_pending_areas_and_carries_its_own_version()
    {
        var seed = Seed("auth.user_offboarding_incomplete.v1");
        var declared = seed.Variables.Select(v => v.Name).ToHashSet();
        var used = Regex
            .Matches(seed.Html + seed.Subject, @"(?:\{\{\s*|\{%\s*(?:if|unless|elsif)\s+)([a-z_][a-z0-9_]*)")
            .Select(m => m.Groups[1].Value)
            .ToHashSet();
        Assert.Empty(used.Except(declared));
        // Plantilla nueva: versión explícita por encima de las del retiro (24), si no el seeder no la publica.
        Assert.Equal(25, seed.ContentVersion);
        Assert.Equal("system-base", seed.LayoutKey);
        Assert.Contains(NotificationTemplateSeedSource.VariablesWithPreheader(seed), v => v.Name == "preheader");

        var context = new TemplateContext();
        context.SetValue("user_name", "Andres Hernandez");
        context.SetValue("pending_modules", new[] { "Calendar", "Chat and meetings" });
        context.SetValue("users_url", "https://manfer.taxoffix.io/company/users");
        context.SetValue("product_name", "TaxOffix");

        Assert.Equal("Andres Hernandez's offboarding didn't finish", Parser.Parse(seed.Subject).Render(context));
        var html = Parser.Parse(seed.Html).Render(context);
        Assert.Contains("these areas haven't", html);
        Assert.Contains("Calendar, Chat and meetings", html);
        Assert.Contains("href=\"https://manfer.taxoffix.io/company/users\"", html);
        // Copy para la oficina: sin nombres internos.
        Assert.DoesNotContain("tenant", seed.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Consumer", seed.Html);

        context.SetValue("pending_modules", new[] { "Calendar" });
        Assert.Contains("this area hasn't", Parser.Parse(seed.Html).Render(context));
    }

    [Fact]
    public void Security_coordinator_template_points_to_Compliance()
    {
        var context = new TemplateContext();
        context.SetValue("settings_url", "https://manfer.taxoffix.io/company/settings#compliance");
        context.SetValue("product_name", "TaxOffix");
        var seed = Seed("tenant.security_coordinator_vacated.v1");

        var html = Parser.Parse(seed.Html).Render(context);

        Assert.Equal("Your office needs a new security coordinator", seed.Subject);
        Assert.Contains("FTC Safeguards Rule", html);
        Assert.Contains("Office settings &rarr; Compliance", html);
        Assert.Contains("href=\"https://manfer.taxoffix.io/company/settings#compliance\"", html);
    }

    private static NotificationTemplateSeed Seed(string key) =>
        Assert.Single(NotificationTemplateSeedSource.All, item => item.EventKey == key);

    private static NotificationTemplateSeed WispSeed() =>
        Assert.Single(NotificationTemplateSeedSource.All, item => item.EventKey == "tenant.wisp_review_due.v1");
}
