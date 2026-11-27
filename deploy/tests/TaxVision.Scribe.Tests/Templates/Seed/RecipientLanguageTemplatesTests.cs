using System.Text.RegularExpressions;
using Fluid;
using TaxVision.Scribe.Application.Templates.Seed;

namespace TaxVision.Scribe.Tests.Templates.Seed;

/// <summary>
/// Plantillas que salen en el idioma de quien lee: con <c>language = "Es"</c>, asunto, cuerpo y preheader en español;
/// sin <c>language</c> (Notification viejo), en inglés como antes. Declaran la variable y suben su versión.
/// </summary>
public sealed class RecipientLanguageTemplatesTests
{
    private const int RecipientLanguageVersion = 37;
    private static readonly FluidParser Parser = new();

    // Clave, asunto y cuerpo en español, asunto y cuerpo en inglés. Agrupadas como en la guía: cliente, staff,
    // citas y reuniones, cuenta, dueño y cobros.
    private static readonly (string Key, string EsSubject, string EsBody, string EnSubject, string EnBody)[] Cases =
    [
        // Al cliente
        (
            "task.waiting_on_client.v1",
            "Nos faltan documentos tuyos",
            "Nos faltan algunos documentos",
            "We're missing documents from you",
            "We're missing some documents"
        ),
        (
            "task.client_request_created.v1",
            "TaxOffix —",
            "Tu contador te pidió documentos",
            "TaxOffix —",
            "Your accountant requested documents"
        ),
        (
            "task.client_request_document_rejected.v1",
            "No pudimos procesar",
            "No pudimos procesar tu archivo",
            "We couldn't process",
            "We couldn't process your file"
        ),
        ("billing.invoice_sent", "Factura", "Aquí tienes tu factura", "Invoice", "here is your invoice"),
        // Trabajo del staff
        ("reminder.due", "Renta", "Abrir TaxOffix", "Renta", "Open TaxOffix"),
        (
            "task.form_request_submitted.v1",
            "Formulario listo para revisión",
            "Un formulario está listo para revisión",
            "Form ready for review",
            "A form is ready for review"
        ),
        ("task.form_action_failed.v1", "requiere atención", "no pudo terminar", "needs attention", "could not finish"),
        (
            "tenant.wisp_review_due.v1",
            "La revisión de tu plan de seguridad vence",
            "vence pronto",
            "Your security plan review is due",
            "is due soon"
        ),
        (
            "auth.user_offboarding_incomplete.v1",
            "no terminó",
            "Un retiro no terminó",
            "didn't finish",
            "An offboarding didn't finish"
        ),
        (
            "tenant.security_coordinator_vacated.v1",
            "nuevo responsable de seguridad",
            "el puesto quedó vacante",
            "new security coordinator",
            "the role is now vacant"
        ),
        (
            "campaign.schedules_paused_on_offboarding.v1",
            "Se pausaron",
            "Se pausaron campañas programadas",
            "scheduled campaigns were paused",
            "Scheduled campaigns were paused"
        ),
        (
            "sig.preparer_attention_required.v1",
            "requiere tu atención",
            "Una solicitud de firma requiere tu atención",
            "needs your attention",
            "A signature request needs your attention"
        ),
        ("sig.preparer_changed.v1", "Nuevo preparador", "Abrir la solicitud", "New preparer", "Open the request"),
        (
            "storage.cloud_connection_review_required.v1",
            "Vuelve a conectar",
            "ya no está en tu oficina",
            "Reconnect your office's",
            "no longer with your office"
        ),
        (
            "connectors.office_mailbox_review_required.v1",
            "Vuelve a conectar el buzón",
            "Vuelve a conectar un buzón de la oficina",
            "Reconnect the office mailbox",
            "Reconnect an office mailbox"
        ),
        // Citas y reuniones
        (
            "communication.meeting.invitation",
            "Invitación:",
            "Te invitaron a una reunión",
            "Invitation:",
            "You've been invited to a meeting"
        ),
        (
            "communication.meeting.cancelled.v1",
            "Cancelada:",
            "La reunión se canceló",
            "Cancelled:",
            "The meeting was cancelled"
        ),
        (
            "communication.meeting.rescheduled.v1",
            "Reprogramada:",
            "La reunión se reprogramó",
            "Rescheduled:",
            "The meeting was rescheduled"
        ),
        (
            "communication.meeting.reminder.v1",
            "Empieza en",
            "Tu reunión empieza en",
            "Starting in",
            "Your meeting starts in"
        ),
        (
            "calendar.appointment_attendee_added.v1",
            "Te agregaron a",
            "Estás en la lista de invitados",
            "You were added to",
            "You're on the guest list"
        ),
        (
            "calendar.appointment_scheduled.v1",
            "TaxOffix —",
            "Tienes una cita nueva",
            "TaxOffix —",
            "You have a new appointment"
        ),
        (
            "calendar.appointment_rescheduled.v1",
            "Tu cita cambió de horario",
            "Ahora:",
            "Your appointment moved",
            "Now:"
        ),
        (
            "calendar.appointment_updated.v1",
            "Actualizada:",
            "Tu cita se actualizó",
            "Updated:",
            "Your appointment was updated"
        ),
        (
            "calendar.booking_declined.v1",
            "No pudimos confirmar",
            "No pudimos confirmar tu cita",
            "We couldn't confirm",
            "We couldn't confirm your appointment"
        ),
        (
            "calendar.appointment_cancelled.v1",
            "Tu cita se canceló",
            "Se canceló una cita que tenías agendada",
            "Your appointment was cancelled",
            "An appointment you had scheduled was cancelled"
        ),
        // Cuenta
        (
            "auth.invitation",
            "Te invitaron a",
            "Te invitaron a colaborar",
            "You've been invited to",
            "You've been invited to collaborate"
        ),
        (
            "auth.password_reset",
            "Restablece tu contraseña",
            "Recibimos una solicitud",
            "Reset your",
            "We received a request"
        ),
        (
            "auth.password-reset.office",
            "te envió un enlace",
            "Un administrador de la oficina",
            "sent you a link",
            "An office administrator at"
        ),
        (
            "auth.account-secured",
            "Acción necesaria: se aseguró tu cuenta",
            "Ya no funciona tu contraseña anterior",
            "Action needed: your",
            "Your previous password no longer works"
        ),
        (
            "auth.password-change-required",
            "te pide actualizar tu contraseña",
            "te pidió elegir una contraseña nueva",
            "asks you to update your password",
            "asked you to choose a new password"
        ),
        (
            "auth.password-changed",
            "Se cambió tu contraseña",
            "Se cambió la contraseña de tu",
            "password was changed",
            "The password for your"
        ),
        ("auth.otp_code", "es tu código de", "Tu código de verificación", "is your", "Your verification code"),
        (
            "auth.email_change",
            "Confirma tu nuevo correo",
            "Pediste cambiar el correo",
            "Confirm your new email",
            "You requested to change the email"
        ),
        (
            "auth.security_alert",
            "Alerta de seguridad",
            "Si reconoces esta actividad",
            "Security alert",
            "If you recognize this activity"
        ),
        (
            "auth.tenant_recovery",
            "Tus oficinas en",
            "Encontramos estas oficinas",
            "Your offices on",
            "We found the following offices"
        ),
        ("auth.welcome", "Te damos la bienvenida", "Tu cuenta está lista", "Welcome to", "your account is ready"),
        // Dueño y cobros
        (
            "seats.user_gated.v1",
            "perdió el acceso",
            "Un miembro del equipo perdió el acceso",
            "lost access",
            "A team member lost access"
        ),
        (
            "seats.user_unlocked.v1",
            "Se restableció el acceso",
            "Acceso restablecido",
            "Access restored",
            "Access restored"
        ),
        (
            "referral.reward.granted",
            "premio de referido",
            "Tu premio de referido ya está en tu Wallet",
            "referral reward",
            "Your referral reward is in your Wallet"
        ),
        (
            "onboarding.otp_code",
            "es tu código de verificación",
            "Tu código de verificación",
            "is your",
            "Your verification code"
        ),
        (
            "onboarding.registration_ready",
            "Tu pago está confirmado",
            "Completa tu cuenta",
            "Your payment is confirmed",
            "Complete your account"
        ),
        (
            "onboarding.receipt_ready",
            "Tu recibo de pago",
            "Tu recibo está listo",
            "payment receipt is ready",
            "Your receipt is ready"
        ),
        (
            "onboarding.payment_failed",
            "Hubo un problema",
            "Tu pago no se completó",
            "There was a problem",
            "Your payment didn't go through"
        ),
        (
            "subscription.discount_ending",
            "Tu descuento de",
            "Tu descuento termina pronto",
            "discount ends in",
            "Your discount ends soon"
        ),
        (
            "subscription.payment_failed",
            "Acción requerida",
            "No pudimos renovar tu suscripción",
            "Action needed",
            "We couldn't renew your subscription"
        ),
        (
            "subscription.suspended",
            "está en pausa",
            "Tu suscripción está en pausa",
            "is on hold",
            "Your subscription is on hold"
        ),
        (
            "subscription.access_ending",
            "Tu acceso a",
            "Tu acceso termina pronto",
            "access ends on",
            "Your access ends soon"
        ),
        (
            "subscription.cancellation_scheduled",
            "termina el",
            "Tu suscripción va a terminar",
            "subscription ends on",
            "Your subscription will end"
        ),
        (
            "subscription.version_change_scheduled",
            "Las condiciones de tu plan",
            "Las condiciones de tu plan van a cambiar",
            "plan terms change",
            "Your plan terms will change"
        ),
        (
            "subscription.billing_cycle_changed",
            "ahora se factura cada mes",
            "Ahora tienes facturación mensual",
            "is now billed every month",
            "You're now on monthly billing"
        ),
        ("subscription.expired", "venció", "Tu suscripción venció", "has expired", "Your subscription has expired"),
        (
            "subscription.renewal_upcoming",
            "se renueva en",
            "Tu suscripción se renueva pronto",
            "renews in",
            "Your subscription renews soon"
        ),
        (
            "subscription.reactivated",
            "está activa de nuevo",
            "qué bueno tenerte de vuelta",
            "is active again",
            "welcome back"
        ),
        ("subscription.recurring_charge", "Pago recibido", "Cobramos", "Payment received", "we charged"),
        (
            "subscription.recurring_charge_failed",
            "Acción requerida",
            "No pudimos procesar tu pago",
            "Action needed",
            "We couldn't process your payment"
        ),
        (
            "subscription.billing_mode_changed",
            "Tu preferencia de facturación cambió",
            "Preferencia de facturación actualizada",
            "Your billing preference changed",
            "Billing preference updated"
        ),
        (
            "subscription.mandate_created",
            "Se activó la renovación automática",
            "Renovación automática activada",
            "Automatic renewal enabled",
            "Automatic renewal enabled"
        ),
        (
            "wallet.low_balance",
            "El saldo de tu Wallet está bajo",
            "Agrega fondos",
            "Your wallet balance is low",
            "Add funds"
        ),
        (
            "wallet.auto_recharge_succeeded",
            "Recibo: recarga automática",
            "Recarga automática completada",
            "automatic recharge",
            "Automatic recharge completed"
        ),
        (
            "wallet.auto_recharge_paused",
            "La recarga automática está en pausa",
            "Pausamos la recarga automática",
            "Automatic recharge is paused",
            "We paused automatic recharge"
        ),
        (
            "wallet.auto_recharge_daily_limit",
            "límite diario",
            "Es el máximo permitido por día",
            "Daily automatic recharge limit",
            "That's the most allowed per day"
        ),
        // Avisos obligatorios a la oficina (D-N48)
        (
            "payments.payout_failed.v1",
            "Acción requerida: falló un depósito",
            "No pudimos depositar",
            "Action needed: a payout",
            "We couldn't send"
        ),
        (
            "payments.connect_account_restricted.v1",
            "tu cuenta de cobros está restringida",
            "Tu cuenta de cobros está restringida",
            "your payments account is restricted",
            "Your payments account is restricted"
        ),
        (
            "payments.connect_account_onboarding_required.v1",
            "Termina de configurar tu cuenta de cobros",
            "Continuar la configuración",
            "Finish setting up your payments account",
            "Continue setup"
        ),
        (
            "storage.file_infected.v1",
            "Alerta de seguridad",
            "Se bloqueó un archivo con malware",
            "Security alert",
            "A file with malware was blocked"
        ),
        (
            "storage.dmca_takedown.v1",
            "Urgente",
            "Se bloqueó un archivo por un aviso DMCA",
            "Urgent",
            "A file was blocked by a DMCA notice"
        ),
        (
            "storage.dmca_counter_notice.v1",
            "Se presentó una contranotificación",
            "Se presentó una contranotificación DMCA",
            "A counter-notice was submitted",
            "A DMCA counter-notice was submitted"
        ),
        ("storage.dmca_reinstated.v1", "Se restauró un archivo", "Archivo restaurado", "was restored", "File restored"),
        (
            "storage.legal_hold_placed.v1",
            "Un archivo quedó en retención legal",
            "no se puede borrar ni vencer",
            "A file was placed on legal hold",
            "can't be deleted or expire"
        ),
        (
            "storage.legal_hold_lifted.v1",
            "Se levantó la retención legal",
            "Retención legal levantada",
            "legal hold on a file was lifted",
            "Legal hold lifted"
        ),
    ];

    public static TheoryData<string> Keys => [.. Cases.Select(c => c.Key)];

    public static TheoryData<string, string, string> Spanish => Data(c => (c.EsSubject, c.EsBody));

    public static TheoryData<string, string, string> English => Data(c => (c.EnSubject, c.EnBody));

    private static TheoryData<string, string, string> Data(
        Func<(string Key, string EsSubject, string EsBody, string EnSubject, string EnBody), (string, string)> pick
    )
    {
        var data = new TheoryData<string, string, string>();
        foreach (var item in Cases)
        {
            var (subject, body) = pick(item);
            data.Add(item.Key, subject, body);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Spanish))]
    public void Renders_spanish_when_language_is_Es(string key, string subject, string body)
    {
        var (renderedSubject, html) = Render(Seed(key), Sample("Es"));

        Assert.Contains(subject, renderedSubject, StringComparison.Ordinal);
        Assert.Contains(body, html, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(English))]
    public void Renders_english_without_language(string key, string subject, string body)
    {
        var (renderedSubject, html) = Render(Seed(key), Sample(language: null));

        Assert.Contains(subject, renderedSubject, StringComparison.Ordinal);
        Assert.Contains(body, html, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public void Declares_language_and_is_republished(string key)
    {
        var seed = Seed(key);

        Assert.Contains(seed.Variables, v => v.Name == "language" && v.DefaultValue == "En");
        // El dominio rechaza un asunto de más de 500 caracteres, y el bilingüe lleva los dos idiomas.
        Assert.True(seed.Subject.Length <= 500, $"{key}: subject has {seed.Subject.Length} characters");
        Assert.True(
            seed.ContentVersion >= RecipientLanguageVersion,
            $"{key}: ContentVersion {seed.ContentVersion} < {RecipientLanguageVersion}"
        );
        var preheader = NotificationTemplateSeedSource
            .VariablesWithPreheader(seed)
            .FirstOrDefault(v => v.Name == "preheader")
            .DefaultValue;
        if (preheader is not null)
            Assert.NotEqual(RenderText(preheader, "Es"), RenderText(preheader, null));
    }

    /// <summary>El español no deja frases en inglés: se mira con todas las variables verdaderas y con todas falsas.</summary>
    [Theory]
    [MemberData(nameof(Keys))]
    public void Spanish_version_leaves_no_english_behind(string key)
    {
        var seed = Seed(key);
        foreach (var truthy in new[] { true, false })
        {
            var vars = new Dictionary<string, object?> { ["language"] = "Es" };
            foreach (var variable in seed.Variables.Where(v => v.Name != "language"))
                vars[variable.Name] = truthy ? "1" : false;

            var (subject, html) = Render(seed, vars);
            var visible = Regex.Replace(subject + " " + html, "<[^>]+>", " ");

            var english = Regex.Matches(
                visible,
                @"\b(the|your|you|and|to|was|we|is|are|has|of|for|with|please|hi)\b",
                RegexOptions.IgnoreCase
            );
            Assert.True(english.Count == 0, $"{key}: {string.Join(", ", english.Select(m => m.Value))} in: {visible}");
        }
    }

    /// <summary>
    /// El correo de código de Auth escribe el motivo en cada idioma a partir de <c>reason_code</c>. Sin código vale
    /// el texto que manda hoy Notification en <c>reason</c>, igual que antes.
    /// </summary>
    [Theory]
    [InlineData("login", "Es", "para <strong style=\"color:#23384B;\">iniciar sesión</strong> es:")]
    [InlineData("email_change", "Es", "para <strong style=\"color:#23384B;\">confirmar tu nuevo correo</strong> es:")]
    [InlineData("phone_verify", "Es", "para <strong style=\"color:#23384B;\">verificar tu teléfono</strong> es:")]
    [InlineData("phone_verification", "Es", "para <strong style=\"color:#23384B;\">verificar tu teléfono</strong> es:")]
    [InlineData("continue", "Es", "para <strong style=\"color:#23384B;\">continuar</strong> es:")]
    [InlineData("login", null, "for <strong style=\"color:#23384B;\">signing in</strong> is:")]
    [InlineData("email_change", null, "for <strong style=\"color:#23384B;\">confirming your new email</strong> is:")]
    [InlineData("phone_verify", null, "for <strong style=\"color:#23384B;\">verifying your phone</strong> is:")]
    [InlineData("continue", null, "for <strong style=\"color:#23384B;\">continuing</strong> is:")]
    [InlineData(null, null, "for <strong style=\"color:#23384B;\">iniciar sesión</strong> is:")]
    [InlineData(null, "Es", "para <strong style=\"color:#23384B;\">iniciar sesión</strong> es:")]
    public void Otp_code_writes_the_reason_in_the_reader_language(string? reasonCode, string? language, string expected)
    {
        var vars = Sample(language);
        vars["code"] = "123456";
        vars["reason"] = "iniciar sesión";
        if (reasonCode is not null)
            vars["reason_code"] = reasonCode;

        var (_, html) = Render(Seed("auth.otp_code"), vars);

        Assert.Contains(expected, html, StringComparison.Ordinal);
    }

    private static Dictionary<string, object?> Sample(string? language)
    {
        var vars = new Dictionary<string, object?>
        {
            ["product_name"] = "TaxOffix",
            ["title"] = "Renta",
            ["cadence"] = "month",
            ["first_name"] = "Ana",
        };
        if (language is not null)
            vars["language"] = language;
        return vars;
    }

    private static string RenderText(string source, string? language)
    {
        var context = new TemplateContext();
        if (language is not null)
            context.SetValue("language", language);
        return Parser.Parse(source).Render(context, NullEncoder.Default);
    }

    [Theory]
    [InlineData(null, "1", "ends in 1 day", "ends in 1 day.")]
    [InlineData(null, "6", "ends in 6 days", "ends in 6 days.")]
    [InlineData("Es", "1", "termina en 1 día", "termina en 1 día.")]
    [InlineData("Es", "6", "termina en 6 días", "termina en 6 días.")]
    public void Discount_ending_agrees_with_the_days_left(string? language, string days, string subject, string body)
    {
        var vars = new Dictionary<string, object?>
        {
            ["discount_label"] = "20%",
            ["days_left"] = days,
            ["billing_url"] = "https://acme.taxoffix.io",
            ["product_name"] = "TaxOffix",
            ["language"] = language,
        };

        var (renderedSubject, html) = Render(Seed("subscription.discount_ending"), vars);

        Assert.EndsWith(subject, renderedSubject);
        Assert.Contains(body, html);
    }

    private static (string Subject, string Html) Render(NotificationTemplateSeed seed, Dictionary<string, object?> vars)
    {
        var context = new TemplateContext();
        foreach (var (key, value) in vars)
            context.SetValue(key, value ?? string.Empty);
        return (
            Parser.Parse(seed.Subject).Render(context, NullEncoder.Default),
            Parser.Parse(seed.Html).Render(context, NullEncoder.Default)
        );
    }

    private static NotificationTemplateSeed Seed(string key) =>
        Assert.Single(NotificationTemplateSeedSource.All, item => item.TemplateKey == key);
}
