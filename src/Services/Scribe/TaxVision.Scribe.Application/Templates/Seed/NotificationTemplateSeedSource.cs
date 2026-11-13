using System.Security.Cryptography;
using System.Text;
using TaxVision.Scribe.Domain.Templates;

namespace TaxVision.Scribe.Application.Templates.Seed;

/// <summary>
/// Definición de un template a sembrar en Scribe (Fase 8 — migración desde Notification): el
/// EventKey por el que Notification renderiza, el TemplateKey heredado del catálogo viejo
/// (<c>EmailTemplates</c>/<c>SignatureTemplateCatalog</c> — mismo string, para no romper la
/// auditoría existente en NotificationLog.TemplateKey), y el HTML/subject Fluid sobre el layout
/// <c>system-base</c>. El HTML es solo el contenido interno: la cáscara (barra, card, wordmark,
/// footer) la aporta el base layout. Cada template abre con eyebrow + acento + H1 (el layout no
/// puede leer el eyebrow, ver BuildLayoutVariables). Idioma por defecto: inglés — los eventos de los
/// 17 templates no-Signature no llevan idioma del destinatario, así que serían ramas muertas. Los 6
/// de Signature sí son bilingües porque su evento trae <c>signer.Language</c>.
/// </summary>
public sealed record NotificationTemplateSeed(
    string EventKey,
    string TemplateKey,
    string Name,
    string Subject,
    string Html,
    IReadOnlyList<(string Name, VariableType Type, bool Required, string? DefaultValue, string? Description)> Variables,
    // Solo etiqueta legible: el seeder republica por ContentHash (política "código manda" para System).
    // v2: se agregó la variable 'preheader' por template (línea de vista previa en el div oculto).
    // v9: plantilla de factura, la primera sobre tenant-base.
    // v11: F7 — nueva plantilla sig.partial_copy.v1 (copia inmediata al firmar).
    // v12: F7 — adapta copy del partial_copy cuando total_signers == 1 + robustez del download_link.
    // v13: F7 — no promete el PDF sellado cuando el preparador no lo va a enviar.
    // v14: sig.invitation.v1 — si la request no tiene expiración (expires_at vacío) dice "no vence"
    //      en vez de "The link expires on  UTC" con la fecha en blanco.
    // v15: la marca sale de {{ product_name }} (PlatformSettings) en vez de "TaxProffice" escrito; los
    //      overrides explícitos subieron uno cada uno por lo mismo.
    // v17: avisos de bloqueo y restauración de acceso por cupo de usuarios.
    // v18 (por plantilla): subscription.recurring_charge_failed reescrito — un correo por cada cobro
    //      automático fallido, con intento, próximo reintento, tarjeta real y lo suspendido al final.
    // v21 (por plantilla): communication.meeting.invitation con título, fecha local, host, código,
    //      rama "in progress" y adjunto .ics (F6).
    // v23 (por plantilla): tenant.wisp_review_due.v1 nueva — revisión anual del WISP (F4 Cumplimiento).
    // v24 (por plantilla): 6 plantillas nuevas de avisos del retiro de empleados (OffboardingNoticesVersion).
    // v25 (por plantilla): auth.user_offboarding_incomplete.v1 nueva — retiro sin todos los acuses en plazo.
    // v26 (por plantilla): calendar.appointment_attendee_added.v1 nueva — a quien suman a una cita ya agendada.
    // v27 (por plantilla): botones Accept / Maybe / Decline (RSVP) en las 3 de cita y en la invitación de sala.
    // v28 (por plantilla): subscription.billing_cycle_changed.v1 nueva — cambio de ciclo (a anual cobrado, a mensual agendado).
    // v29 (por plantilla): subscription.renewal_upcoming muestra el total (plan + extras) y el ciclo; aviso de 30 días al anual.
    // v30 (por plantilla): 6 plantillas nuevas de la demo pública del landing (PublicDemoVersion).
    // v31 (por plantilla): demo.confirmed con "Add to calendar" principal y demo.already_booked nueva (PublicDemoCtaVersion).
    int ContentVersion = 1,
    /// <summary>
    /// Layout sobre el que se publica. <c>system-base</c> para lo que manda la plataforma;
    /// <c>tenant-base</c> para lo que manda la OFICINA (su cáscara y su logo), como la factura.
    /// El scope sigue siendo System: una sola plantilla para todas, no una copia por oficina.
    /// </summary>
    string LayoutKey = "system-base",
    TemplateChannel Channel = TemplateChannel.Email,
    string? Text = null
)
{
    /// <summary>
    /// SCR-01: huella de lo que se publica; es lo que decide republicar. Incluye el preheader, que vive en su
    /// propio diccionario, y normaliza los fines de línea para no depender de la máquina que compiló.
    /// </summary>
    public string ContentHash
    {
        get
        {
            var variables = NotificationTemplateSeedSource
                .VariablesWithPreheader(this)
                .Select(v => $"{v.Name}\u001f{v.Type}\u001f{v.Required}\u001f{v.DefaultValue}\u001f{v.Description}")
                .Order(StringComparer.Ordinal);
            var content = string.Join(
                "\n",
                Subject,
                Html,
                Text,
                string.Join('\u001e', variables),
                LayoutKey,
                Channel.ToString()
            );
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content.ReplaceLineEndings("\n"))));
        }
    }
}

/// <summary>
/// Los 23 templates que Notification renderizaba localmente convertidos a Fluid HTML sobre
/// <c>system-base</c>. Sembrados por <c>ScribeNotificationTemplateSeeder</c> al arranque. El texto
/// plano no se declara aparte — el renderer cae a strip-tags automático del HTML cuando
/// <c>TextFileId</c> es null (ver FluidTemplateRenderer.ResolveTextAsync).
/// </summary>
public static partial class NotificationTemplateSeedSource
{
    // Propiedad computada (no field initializer): "All" aparece antes que las definiciones en este
    // archivo, y los field initializers de una clase estática corren en orden textual — un
    // `{ get; } = [...]` aquí capturaría null en cada una. `=>` evalúa on-access, ya inicializado.
    public static IReadOnlyList<NotificationTemplateSeed> All =>
        [
            InvoiceSent,
            Invitation,
            PasswordReset,
            OtpCode,
            EmailChange,
            SecurityAlert,
            TenantRecovery,
            Welcome,
            SignatureInvitation,
            SignatureReminder,
            SignatureCompleted,
            SignaturePartialCopy,
            SignatureCertificateReady,
            SignatureExpired,
            SignatureDeclined,
            SignatureVerificationChallenge,
            OnboardingOtpRequested,
            GiftcardShare,
            OnboardingRegistrationReady,
            OnboardingReceiptReady,
            OnboardingPaymentFailed,
            SubscriptionPaymentFailed,
            SubscriptionSuspended,
            SubscriptionExpired,
            SubscriptionCancellationScheduled,
            SubscriptionVersionChangeScheduled,
            SubscriptionBillingCycleChanged,
            SubscriptionAccessEnding,
            SubscriptionRenewalUpcoming,
            SubscriptionReactivated,
            SubscriptionRecurringCharge,
            SubscriptionRecurringChargeFailed,
            SubscriptionBillingModeChanged,
            SubscriptionMandateCreated,
            UserSlotGated,
            UserSlotUnlocked,
            WispReviewDue,
            SecurityCoordinatorVacated,
            CampaignSchedulesPaused,
            SignaturePreparerAttention,
            SignaturePreparerChanged,
            CloudConnectionReviewRequired,
            OfficeMailboxReviewRequired,
            OffboardingIncomplete,
            ReminderDue,
            TaskWaitingOnClient,
            AppointmentScheduled,
            AppointmentAttendeeAdded,
            AppointmentRescheduled,
            AppointmentUpdated,
            BookingDeclined,
            AppointmentCancelled,
            ClientRequestCreated,
            ClientRequestDocumentRejected,
            FormAccessLink,
            FormAccessCode,
            FormRequestAssigned,
            FormRequestReturned,
            FormRequestCompleted,
            FormAttachmentRejected,
            FormRequestReminder,
            FormParticipantReturned,
            FormParticipantReminder,
            FormParticipantCompleted,
            FormParticipantCancelled,
            FormParticipantAttachmentRejected,
            FormRequestSubmitted,
            FormActionFailed,
            FormRequestAssignedPush,
            FormRequestReturnedPush,
            FormRequestReminderPush,
            FormAttachmentRejectedPush,
            FormRequestCompletedPush,
            FormRequestSubmittedPush,
            FormActionFailedPush,
            SharedFileInvited,
            MeetingInvitation,
            MeetingCancelled,
            MeetingRescheduled,
            MeetingReminder,
            PublicDemoVerificationCode,
            PublicDemoConfirmed,
            PublicDemoNewBooking,
            PublicDemoRescheduled,
            PublicDemoCancelled,
            PublicDemoReminder,
            PublicDemoAlreadyBooked,
        ];

    // v2: tildes en el español de los push.
    private const int PushVersion = 2;

    // v21: los emails de Client Forms a clientes y externos responden a language (P-21.5).
    // v22: el preheader también tiene rama en español.
    private const int ClientFormsBilingualVersion = 22;

    private static (string, VariableType, bool, string?, string?) Language =>
        ("language", VariableType.String, false, "En", "Recipient language: Es or En.");

    // Notification manda el texto fijo del código de rechazo en los dos idiomas, nunca el código.
    private const string RejectionMessage =
        "{% if language == 'Es' and client_message_es %}{{ client_message_es }}{% else %}{{ client_message }}{% endif %}";

    private static IReadOnlyList<(string, VariableType, bool, string?, string?)> PushVariables =>
        [("language", VariableType.String, true, "En", "Recipient language: Es or En.")];

    private static NotificationTemplateSeed FormRequestAssignedPush { get; } =
        Push(
            "task.form_request_assigned.push.v1",
            "Client Forms - Assigned push",
            "{% if language == 'Es' %}Nuevo formulario por completar{% else %}New form to complete{% endif %}",
            "{% if language == 'Es' %}Tu oficina de impuestos te envió un formulario.{% else %}Your tax office sent you a form.{% endif %}"
        );

    private static NotificationTemplateSeed FormRequestReturnedPush { get; } =
        Push(
            "task.form_request_returned.push.v1",
            "Client Forms - Returned push",
            "{% if language == 'Es' %}Cambios solicitados{% else %}Changes requested{% endif %}",
            "{% if language == 'Es' %}Tu oficina solicitó cambios en un formulario.{% else %}Your office asked for changes on a form.{% endif %}"
        );

    private static NotificationTemplateSeed FormRequestReminderPush { get; } =
        new(
            EventKey: "task.form_request_reminder.push.v1",
            TemplateKey: "task.form_request_reminder.push.v1",
            Name: "Client Forms - Reminder push",
            Subject: "{% if language == 'Es' %}{% if is_overdue %}Formulario vencido{% else %}Formulario por vencer{% endif %}{% else %}{% if is_overdue %}Form overdue{% else %}Form due soon{% endif %}{% endif %}",
            Html: string.Empty,
            Variables:
            [
                ("language", VariableType.String, true, "En", "Recipient language: Es or En."),
                ("is_overdue", VariableType.Bool, true, "false", "Whether the form is overdue."),
            ],
            ContentVersion: PushVersion,
            Channel: TemplateChannel.Push,
            Text: "{% if language == 'Es' %}Inicia sesión para terminar tu formulario.{% else %}Sign in to finish your form.{% endif %}"
        );

    private static NotificationTemplateSeed FormAttachmentRejectedPush { get; } =
        Push(
            "task.form_attachment_rejected.push.v1",
            "Client Forms - File rejected push",
            "{% if language == 'Es' %}Archivo no aceptado{% else %}File not accepted{% endif %}",
            "{% if language == 'Es' %}Carga el archivo nuevamente.{% else %}Please upload the file again.{% endif %}"
        );

    private static NotificationTemplateSeed FormRequestCompletedPush { get; } =
        Push(
            "task.form_request_completed.push.v1",
            "Client Forms - Completed push",
            "{% if language == 'Es' %}Formulario recibido{% else %}Form received{% endif %}",
            "{% if language == 'Es' %}Tu oficina marcó el formulario como completado.{% else %}Your office marked your form as complete.{% endif %}"
        );

    private static NotificationTemplateSeed FormRequestSubmittedPush { get; } =
        Push(
            "task.form_request_submitted.push.v1",
            "Client Forms - Submitted push",
            "{% if language == 'Es' %}Nuevo formulario enviado{% else %}New form submission{% endif %}",
            "{% if language == 'Es' %}Un cliente envió un formulario para revisión.{% else %}A client submitted a form for review.{% endif %}"
        );

    private static NotificationTemplateSeed FormActionFailedPush { get; } =
        Push(
            "task.form_action_failed.push.v1",
            "Client Forms - Action failed push",
            "{% if language == 'Es' %}Una acción requiere atención{% else %}Action needs attention{% endif %}",
            "{% if language == 'Es' %}Una acción de formulario requiere revisión.{% else %}A form action needs your review.{% endif %}"
        );

    private static NotificationTemplateSeed Push(string key, string name, string subject, string text) =>
        new(
            EventKey: key,
            TemplateKey: key,
            Name: name,
            Subject: subject,
            Html: string.Empty,
            Variables: PushVariables,
            ContentVersion: PushVersion,
            Channel: TemplateChannel.Push,
            Text: text
        );

    // Preheader (línea de vista previa que muestran el inbox y la notificación) por template, en inglés
    // (idioma por defecto). Se inyecta como variable 'preheader' con default vía VariablesWithPreheader; el
    // layout la imprime en su div oculto. Un default con Liquid (Bilingual) lo renderiza el renderer con las
    // variables del envío; así los de Client Forms respetan language. Sin entrada acá, cae al subject.
    private static readonly IReadOnlyDictionary<string, string> Preheaders = new Dictionary<string, string>(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["auth.invitation"] = "You've been invited to collaborate — activate your account to get started.",
        ["auth.password_reset"] = "Use the secure link inside to reset your password. It expires soon.",
        ["auth.otp_code"] = "Here's your one-time verification code. It's valid for a few minutes.",
        ["auth.email_change"] = "Confirm your new email address to keep your account secure.",
        ["auth.security_alert"] = "We noticed important activity on your account. Please review it.",
        ["auth.tenant_recovery"] = "Here are the offices linked to your email so you can get back in.",
        ["auth.welcome"] = "Your account is ready — here's how to make the most of it.",
        ["sig.invitation.v1"] = "A document is waiting for your signature. It only takes a minute.",
        ["sig.reminder.v1"] = "A friendly reminder: your signature is still pending.",
        ["sig.completed.v1"] = "All signatures are in — your document is complete.",
        ["sig.partial_copy.v1"] = "Here's your signed copy — others may still be finishing.",
        ["sig.certificate.v1"] = "Your signature certificate of completion is ready to download.",
        ["sig.expired.v1"] = "This signature request has expired. Reach out if you still need to sign.",
        ["sig.declined.v1"] = "A signature request was cancelled. Here are the details.",
        ["sig.verification-challenge.v1"] = "Here's your verification code to continue signing securely.",
        ["onboarding.otp_code"] = "Here's your verification code to continue setting up your account.",
        ["onboarding.registration_ready"] = "Payment confirmed — finish creating your account to get started.",
        ["onboarding.receipt_ready"] = "Your payment receipt is ready to view and download.",
        ["onboarding.payment_failed"] = "Your payment didn't go through — you can try again in a moment.",
        ["subscription.payment_failed"] = "We couldn't renew your subscription — update your payment to keep access.",
        ["subscription.suspended"] = "Your subscription is on hold. Renew to restore your team's access.",
        ["subscription.expired"] = "Your subscription has expired. Renew whenever you're ready.",
        ["subscription.cancellation_scheduled"] = "Your subscription is set to end. You keep full access until then.",
        ["subscription.version_change_scheduled"] = "Your plan terms change at your next renewal. Here's what changes.",
        ["subscription.renewal_upcoming"] = "Your subscription renews soon — nothing to do, just a heads up.",
        ["subscription.reactivated"] = "You're all set — your subscription is active again.",
        ["subscription.recurring_charge"] = "Payment received — your subscription continues.",
        ["subscription.recurring_charge_failed"] =
            "Your payment didn't go through. See what happens next and how to keep your access.",
        ["subscription.billing_mode_changed"] = "Your billing preference was updated.",
        ["subscription.mandate_created"] = "Automatic renewal is now enabled on your subscription.",
        ["seats.user_gated.v1"] = "A team member lost access because the office is over its active seat limit.",
        ["seats.user_unlocked.v1"] = "A team member can access the office again.",
        ["tenant.wisp_review_due.v1"] =
            "Your annual security plan (WISP) review needs attention. Mark it reviewed in Office settings.",
        ["tenant.security_coordinator_vacated.v1"] =
            "Your security coordinator is no longer with your office. Choose a new one in Office settings.",
        ["campaign.schedules_paused_on_offboarding.v1"] =
            "Some scheduled campaigns were paused. Cancel them or schedule them again.",
        ["sig.preparer_attention_required.v1"] =
            "A signature request needs your attention because its preparer is no longer with your office.",
        ["sig.preparer_changed.v1"] = "A signature request has a new preparer.",
        ["storage.cloud_connection_review_required.v1"] =
            "Your office's cloud storage connection needs to be reconnected with an office account.",
        ["connectors.office_mailbox_review_required.v1"] =
            "An office mailbox needs to be reconnected with an account your office controls.",
        ["auth.user_offboarding_incomplete.v1"] =
            "Part of an offboarding hasn't confirmed yet. Some work or access may still be active.",
        ["reminder.due"] = "A quick reminder about something on your list.",
        ["task.waiting_on_client.v1"] = "We're still missing a few documents from you to move forward.",
        ["calendar.appointment_scheduled.v1"] = "Your appointment is booked. Here are the details.",
        ["calendar.appointment_attendee_added.v1"] = "You were added to an appointment. Here are the details.",
        ["calendar.appointment_rescheduled.v1"] = "Your appointment time has changed. See the new details.",
        ["calendar.appointment_updated.v1"] = "Your appointment details changed. Here is what is new.",
        ["calendar.booking_declined.v1"] = "Your office couldn't confirm the time you asked for.",
        ["calendar.appointment_cancelled.v1"] = "Your appointment was cancelled. Here's what happened.",
        ["task.client_request_created.v1"] = "Your preparer requested a few items from you. Here's what's needed.",
        ["task.client_request_document_rejected.v1"] = "We couldn't process a file you uploaded. Please try again.",
        ["task.form_access_link.v1"] = Bilingual(
            "Abre el enlace seguro para verificar tu identidad y completar el formulario.",
            "Open the secure link inside to verify your identity and complete the form."
        ),
        ["task.form_access_code.v1"] = Bilingual(
            "Usa este código de un solo uso para continuar de forma segura. Vence pronto.",
            "Use this one-time code to continue securely. It expires soon."
        ),
        ["task.form_request_assigned.v1"] = Bilingual(
            "Tu oficina de impuestos necesita información tuya.",
            "Your tax office needs information from you."
        ),
        ["task.form_request_returned.v1"] = Bilingual(
            "Tu oficina de impuestos te pidió actualizar un formulario enviado.",
            "Your tax office asked you to update a submitted form."
        ),
        ["task.form_request_completed.v1"] = Bilingual(
            "Tu oficina de impuestos recibió y completó tu formulario.",
            "Your tax office received and completed your form."
        ),
        ["task.form_attachment_rejected.v1"] = Bilingual(
            "Hace falta volver a subir un archivo.",
            "A file needs to be uploaded again."
        ),
        ["task.form_request_reminder.v1"] = Bilingual(
            "Un formulario que pidió tu oficina de impuestos sigue pendiente.",
            "A form requested by your tax office is still waiting."
        ),
        ["task.form_participant_returned.v1"] = Bilingual(
            "Tu oficina de impuestos te pidió actualizar un formulario que enviaste.",
            "Your tax office asked you to update a form you sent."
        ),
        ["task.form_participant_completed.v1"] = Bilingual(
            "Tu oficina de impuestos recibió tu formulario.",
            "Your tax office received your form."
        ),
        ["task.form_participant_cancelled.v1"] = Bilingual(
            "Se canceló una solicitud de formulario de tu oficina de impuestos.",
            "A form request from your tax office was cancelled."
        ),
        ["task.form_participant_reminder.v1"] = Bilingual(
            "Un formulario de tu oficina de impuestos sigue pendiente.",
            "A form from your tax office is still waiting."
        ),
        ["task.form_participant_attachment_rejected.v1"] = Bilingual(
            "Hace falta volver a enviar un archivo que subiste.",
            "A file you uploaded needs to be sent again."
        ),
        ["storage.share_invited.v1"] = "A document was shared with you — open it securely with the link inside.",
        ["communication.meeting.invitation"] = "You've been invited to a meeting — join with the link inside.",
        ["communication.meeting.cancelled.v1"] = "A meeting you were invited to was cancelled.",
        ["communication.meeting.rescheduled.v1"] = "A meeting you were invited to has a new time.",
        ["communication.meeting.reminder.v1"] = "Your meeting is about to start.",
        ["demo.verification_code.v1"] = Bilingual(
            "Tu código para confirmar la demo. Vence en pocos minutos.",
            "Your code to confirm the demo. It expires in a few minutes."
        ),
        ["demo.confirmed.v1"] = Bilingual(
            "Tu demo quedó agendada: aquí tienes el enlace y la hora.",
            "Your demo is booked: here's the link and the time."
        ),
        ["demo.new_booking.v1"] = "A prospect booked a demo with you. Their details are inside.",
        ["demo.rescheduled.v1"] = Bilingual("La demo tiene un nuevo horario.", "The demo has a new time."),
        ["demo.cancelled.v1"] = Bilingual("La demo fue cancelada.", "The demo was canceled."),
        ["demo.already_booked.v1"] = Bilingual(
            "Tu demo sigue en pie. Aquí tienes un enlace nuevo para cambiarla.",
            "Your demo is still on. Here's a new link to change it."
        ),
        ["demo.reminder.v1"] = Bilingual(
            "Tu demo se acerca. Aquí tienes el enlace para entrar.",
            "Your demo is coming up. Here's the link to join."
        ),
    };

    // Preheader con rama por idioma: el renderer lo evalúa con las variables del envío.
    private static string Bilingual(string spanish, string english) =>
        $"{{% if language == 'Es' %}}{spanish}{{% else %}}{english}{{% endif %}}";

    /// <summary>
    /// Variables del template + la variable <c>preheader</c> (default = su línea de vista previa del
    /// diccionario <see cref="Preheaders"/>). El seeder usa esto en vez de <c>def.Variables</c> para
    /// persistir el preheader por versión sin tener que editar cada bloque Variables. Sin entrada en el
    /// diccionario devuelve las variables tal cual (el renderer caerá al subject).
    /// </summary>
    public static IReadOnlyList<(
        string Name,
        VariableType Type,
        bool Required,
        string? DefaultValue,
        string? Description
    )> VariablesWithPreheader(NotificationTemplateSeed definition)
    {
        if (!Preheaders.TryGetValue(definition.TemplateKey, out var preheader))
            return definition.Variables;

        return
        [
            .. definition.Variables,
            (
                "preheader",
                VariableType.String,
                false,
                preheader,
                "Preview/preheader line shown in the inbox and push notifications."
            ),
        ];
    }

    private static NotificationTemplateSeed UserSlotGated { get; } =
        new(
            EventKey: "seats.user_gated.v1",
            TemplateKey: "seats.user_gated.v1",
            Name: "Seats — Team member access blocked",
            Subject: "A team member lost access to {{ tenantName }}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Team access</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#D97706" style="background-color:#D97706;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;color:#23384B;mso-line-height-rule:exactly;">A team member lost access</td></tr>
              <tr><td style="padding-bottom:16px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;"><strong style="color:#23384B;">{{ userFullName }}</strong> ({{ userEmail }}) can no longer access the office because seat #{{ slotIndex }} is suspended. Pay the add-on seat or deactivate another user to free a slot.</td></tr>
              <tr>
                <td align="left" style="padding:12px 0 4px 0;">
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ billingUrl }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Open billing</a></td></tr></table>
                </td>
              </tr>
            </table>
            """,
            Variables:
            [
                ("userFullName", VariableType.String, true, null, "Full name of the gated team member."),
                ("userEmail", VariableType.String, true, null, "Email of the gated team member."),
                ("slotIndex", VariableType.Number, true, null, "Deterministic seat position."),
                ("effectiveCap", VariableType.Number, true, null, "Current active seat limit."),
                ("tenantName", VariableType.String, true, null, "Office name."),
                ("billingUrl", VariableType.Url, true, null, "Subscription management URL."),
            ]
        );

    private static NotificationTemplateSeed UserSlotUnlocked { get; } =
        new(
            EventKey: "seats.user_unlocked.v1",
            TemplateKey: "seats.user_unlocked.v1",
            Name: "Seats — Team member access restored",
            Subject: "Access restored for {{ userFullName }}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Team access</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#159A72" style="background-color:#159A72;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;color:#23384B;mso-line-height-rule:exactly;">Access restored</td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;"><strong style="color:#23384B;">{{ userFullName }}</strong> can access {{ tenantName }} again after the seat was reactivated.</td></tr>
            </table>
            """,
            Variables:
            [
                ("userFullName", VariableType.String, true, null, "Full name of the restored team member."),
                ("userEmail", VariableType.String, false, null, "Email of the restored team member."),
                ("tenantName", VariableType.String, true, null, "Office name."),
            ]
        );

    /// <summary>
    /// F4 Cumplimiento — la revisión anual del WISP está por vencer (DueSoon) o venció (Overdue). Va al
    /// responsable de seguridad y a quienes administran la oficina; la manda la plataforma (system-base).
    /// </summary>
    private static NotificationTemplateSeed WispReviewDue { get; } =
        new(
            EventKey: "tenant.wisp_review_due.v1",
            TemplateKey: "tenant.wisp_review_due.v1",
            Name: "Tenant — WISP annual review due",
            Subject: "{% if is_overdue %}Your security plan review is overdue{% else %}Your security plan review is due {{ due_date }}{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Compliance</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#D97706" style="background-color:#D97706;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;color:#23384B;mso-line-height-rule:exactly;">{% if is_overdue %}Your security plan review is overdue{% else %}Your security plan review is due soon{% endif %}</td></tr>
              <tr><td style="padding-bottom:14px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">{% if is_overdue %}The annual review of your office's written information security plan (WISP) was due on <strong style="color:#23384B;">{{ due_date }}</strong>.{% else %}The annual review of your office's written information security plan (WISP) is due on <strong style="color:#23384B;">{{ due_date }}</strong>.{% endif %} The FTC Safeguards Rule requires reviewing the WISP at least once a year.</td></tr>
              <tr><td style="padding-bottom:4px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Once you've reviewed it, open <strong style="color:#23384B;">Office settings &rarr; Compliance</strong> and mark it reviewed.</td></tr>
              <tr>
                <td align="left" style="padding:26px 0 18px 0;">
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ settings_url }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Open Compliance settings</a></td></tr></table>
                </td>
              </tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">You're receiving this because you're the security coordinator or an administrator of your office on {{ product_name }}.</td></tr>
            </table>
            """,
            Variables:
            [
                ("is_overdue", VariableType.Bool, true, "false", "true when the review date already passed."),
                ("due_date", VariableType.String, true, null, "Review due date, already formatted (en-US)."),
                ("settings_url", VariableType.Url, true, null, "Office settings → Compliance in the office CRM."),
                (
                    "product_name",
                    VariableType.String,
                    false,
                    "TaxOffix",
                    "Branding del producto (lo inyecta el renderer desde PlatformSettings)."
                ),
            ],
            ContentVersion: 23
        );

    // ---------- Avisos del retiro de empleados (offboard): al personal de la oficina, system-base ----------

    // Versión propia de las 6 plantillas del retiro: explícita y mayor que el default (22) y que el WISP (23).
    private const int OffboardingNoticesVersion = 24;

    // Plantilla nueva del retiro con acuse: versión explícita propia, por encima de las anteriores.
    private const int OffboardingIncompleteVersion = 25;

    /// <summary>Un retiro no recibió el acuse de todas las partes en el plazo (Auth, barrido del retiro).</summary>
    private static NotificationTemplateSeed OffboardingIncomplete { get; } =
        new(
            EventKey: "auth.user_offboarding_incomplete.v1",
            TemplateKey: "auth.user_offboarding_incomplete.v1",
            Name: "Auth — Offboarding didn't finish",
            Subject: "{{ user_name }}'s offboarding didn't finish",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Team</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#D97706" style="background-color:#D97706;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;color:#23384B;mso-line-height-rule:exactly;">An offboarding didn't finish</td></tr>
              <tr><td style="padding-bottom:14px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;"><strong style="color:#23384B;">{{ user_name }}</strong> was offboarded, but {% if pending_modules.size == 1 %}this area hasn't{% else %}these areas haven't{% endif %} confirmed the handover yet:</td></tr>
              <tr><td style="padding-bottom:16px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF" style="background-color:#EAF4FF;border-radius:10px;"><tr><td style="padding:14px 18px;border-left:3px solid #67BAF4;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#23384B;font-weight:bold;mso-line-height-rule:exactly;">{{ pending_modules | join: ", " }}</td></tr></table></td></tr>
              <tr><td style="padding-bottom:4px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Some of their work or access there may still be active. Review it, and contact support if it doesn't clear up.</td></tr>
              <tr>
                <td align="left" style="padding:26px 0 18px 0;">
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ users_url }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Open Users</a></td></tr></table>
                </td>
              </tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">You're receiving this because you manage your office's users on {{ product_name }}.</td></tr>
            </table>
            """,
            Variables:
            [
                ("user_name", VariableType.String, true, null, "Name of the person who was offboarded."),
                ("pending_modules", VariableType.String, true, null, "List of the areas that haven't confirmed."),
                ("users_url", VariableType.Url, true, null, "Users page in the office CRM."),
                ProductName,
            ],
            ContentVersion: OffboardingIncompleteVersion
        );

    /// <summary>El responsable de seguridad (WISP) fue retirado y la designación quedó vacante.</summary>
    private static NotificationTemplateSeed SecurityCoordinatorVacated { get; } =
        new(
            EventKey: "tenant.security_coordinator_vacated.v1",
            TemplateKey: "tenant.security_coordinator_vacated.v1",
            Name: "Tenant — Security coordinator vacant",
            Subject: "Your office needs a new security coordinator",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Compliance</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#D97706" style="background-color:#D97706;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;color:#23384B;mso-line-height-rule:exactly;">Your office needs a new security coordinator</td></tr>
              <tr><td style="padding-bottom:14px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">The person designated as your office's security coordinator is no longer with your office, so the role is now vacant.</td></tr>
              <tr><td style="padding-bottom:4px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">The FTC Safeguards Rule requires every office to designate someone to oversee its information security plan (WISP). Choose a new coordinator in <strong style="color:#23384B;">Office settings &rarr; Compliance</strong>.</td></tr>
              <tr>
                <td align="left" style="padding:26px 0 18px 0;">
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ settings_url }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Choose a coordinator</a></td></tr></table>
                </td>
              </tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">You're receiving this because you're an administrator of your office on {{ product_name }}.</td></tr>
            </table>
            """,
            Variables:
            [
                ("settings_url", VariableType.Url, true, null, "Office settings → Compliance in the office CRM."),
                ProductName,
            ],
            ContentVersion: OffboardingNoticesVersion
        );

    /// <summary>Campaigns pausó los agendados de un empleado retirado sin sucesor.</summary>
    private static NotificationTemplateSeed CampaignSchedulesPaused { get; } =
        new(
            EventKey: "campaign.schedules_paused_on_offboarding.v1",
            TemplateKey: "campaign.schedules_paused_on_offboarding.v1",
            Name: "Campaigns — Schedules paused after offboarding",
            Subject: "{% if campaign_count == 1 %}A scheduled campaign was paused{% else %}{{ campaign_count }} scheduled campaigns were paused{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Campaigns</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#D97706" style="background-color:#D97706;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;color:#23384B;mso-line-height-rule:exactly;">{% if campaign_count == 1 %}A scheduled campaign was paused{% else %}Scheduled campaigns were paused{% endif %}</td></tr>
              <tr><td style="padding-bottom:14px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">{% if campaign_count == 1 %}This campaign was{% else %}These campaigns were{% endif %} scheduled by someone who's no longer with your office. So nothing goes out in their name, we paused {% if campaign_count == 1 %}it{% else %}them{% endif %}:</td></tr>
              <tr><td style="padding-bottom:16px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF" style="background-color:#EAF4FF;border-radius:10px;"><tr><td style="padding:14px 18px;border-left:3px solid #67BAF4;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#23384B;font-weight:bold;mso-line-height-rule:exactly;">{{ campaign_names | join: ", " }}</td></tr></table></td></tr>
              <tr><td style="padding-bottom:4px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Open Campaigns to cancel {% if campaign_count == 1 %}it{% else %}them{% endif %} or schedule {% if campaign_count == 1 %}it{% else %}them{% endif %} again. Nothing will be sent until you do.</td></tr>
              <tr>
                <td align="left" style="padding:26px 0 18px 0;">
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ campaigns_url }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Open Campaigns</a></td></tr></table>
                </td>
              </tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">You're receiving this because you manage campaigns for your office on {{ product_name }}.</td></tr>
            </table>
            """,
            Variables:
            [
                ("campaign_count", VariableType.Number, true, null, "How many different campaigns were paused."),
                ("campaign_names", VariableType.String, true, null, "List of the paused campaign names."),
                ("campaigns_url", VariableType.Url, true, null, "Campaigns page in the office CRM."),
                ProductName,
            ],
            ContentVersion: OffboardingNoticesVersion
        );

    /// <summary>Una solicitud de firma necesita otro preparador (o revisión) por el retiro de un empleado.</summary>
    private static NotificationTemplateSeed SignaturePreparerAttention { get; } =
        new(
            EventKey: "sig.preparer_attention_required.v1",
            TemplateKey: "sig.preparer_attention_required.v1",
            Name: "Signature — Preparer needs attention after offboarding",
            Subject: "{% if reason == 'PreparerCleared' %}Choose a new preparer for “{{ request_title }}”{% elsif reason == 'PreparerChangeBlocked' %}“{{ request_title }}” needs a new preparer{% elsif reason == 'UnsentRequestKept' %}Review the unsent request “{{ request_title }}”{% else %}“{{ request_title }}” needs your attention{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Signatures</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#D97706" style="background-color:#D97706;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;color:#23384B;mso-line-height-rule:exactly;">{% if reason == 'PreparerCleared' %}Choose a new preparer{% elsif reason == 'PreparerChangeBlocked' %}This request needs a new preparer{% elsif reason == 'UnsentRequestKept' %}Review an unsent request{% else %}A signature request needs your attention{% endif %}</td></tr>
              <tr><td style="padding-bottom:14px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">{% if reason == 'PreparerCleared' %}The preparer of <strong style="color:#23384B;">{{ request_title }}</strong> is no longer with your office, so they were removed from it. Choose a new preparer before you send it.{% elsif reason == 'PreparerChangeBlocked' %}The preparer of <strong style="color:#23384B;">{{ request_title }}</strong> is no longer with your office and hasn't signed yet. Change the preparer so the return can be signed.{% elsif reason == 'UnsentRequestKept' %}<strong style="color:#23384B;">{{ request_title }}</strong> was created by someone who's no longer with your office and hasn't been sent. Review it and decide whether to send it or cancel it.{% else %}Someone who worked on <strong style="color:#23384B;">{{ request_title }}</strong> is no longer with your office. Open it to see what needs to change.{% endif %}</td></tr>
              <tr>
                <td align="left" style="padding:22px 0 18px 0;">
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ request_url }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Open the request</a></td></tr></table>
                </td>
              </tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">You're receiving this because you work with signature requests at your office on {{ product_name }}.</td></tr>
            </table>
            """,
            Variables:
            [
                (
                    "reason",
                    VariableType.String,
                    true,
                    null,
                    "PreparerCleared, PreparerChangeBlocked or UnsentRequestKept."
                ),
                ("request_title", VariableType.String, true, null, "Title of the signature request."),
                ("request_url", VariableType.Url, true, null, "The signature request in the office CRM."),
                ProductName,
            ],
            ContentVersion: OffboardingNoticesVersion
        );

    /// <summary>El preparador de una solicitud enviada pasó a otra persona: aviso al nuevo preparador o al autor.</summary>
    private static NotificationTemplateSeed SignaturePreparerChanged { get; } =
        new(
            EventKey: "sig.preparer_changed.v1",
            TemplateKey: "sig.preparer_changed.v1",
            Name: "Signature — Preparer changed",
            Subject: "{% if is_new_preparer %}You're now the preparer for “{{ request_title }}”{% else %}New preparer for “{{ request_title }}”{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Signatures</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;color:#23384B;mso-line-height-rule:exactly;">{% if is_new_preparer %}You're now the preparer{% else %}New preparer{% endif %}</td></tr>
              <tr><td style="padding-bottom:14px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">{% if is_offboarding %}The previous preparer of <strong style="color:#23384B;">{{ request_title }}</strong> is no longer with your office. {% endif %}{% if is_new_preparer %}You'll sign {% if is_offboarding %}this return{% else %}<strong style="color:#23384B;">{{ request_title }}</strong>{% endif %} as its preparer. Open the request to review it.{% else %}<strong style="color:#23384B;">{{ preparer_name }}</strong> is now the preparer of {% if is_offboarding %}this signature request{% else %}<strong style="color:#23384B;">{{ request_title }}</strong>{% endif %}.{% endif %}</td></tr>
              <tr>
                <td align="left" style="padding:22px 0 18px 0;">
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ request_url }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Open the request</a></td></tr></table>
                </td>
              </tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">You're receiving this because you're the preparer or the author of this signature request on {{ product_name }}.</td></tr>
            </table>
            """,
            Variables:
            [
                (
                    "is_new_preparer",
                    VariableType.Bool,
                    true,
                    "false",
                    "true for the new preparer; false for the request author."
                ),
                (
                    "is_offboarding",
                    VariableType.Bool,
                    true,
                    "false",
                    "true when the change was caused by the previous preparer leaving the office."
                ),
                ("request_title", VariableType.String, true, null, "Title of the signature request."),
                ("preparer_name", VariableType.String, true, null, "Name of the new preparer."),
                ("request_url", VariableType.Url, true, null, "The signature request in the office CRM."),
                ProductName,
            ],
            ContentVersion: OffboardingNoticesVersion
        );

    /// <summary>Una conexión de nube de la oficina quedó ligada a un empleado retirado.</summary>
    private static NotificationTemplateSeed CloudConnectionReviewRequired { get; } =
        new(
            EventKey: "storage.cloud_connection_review_required.v1",
            TemplateKey: "storage.cloud_connection_review_required.v1",
            Name: "Storage — Cloud connection needs to be reconnected",
            Subject: "Reconnect your office's {{ provider_name }} connection",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Storage</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#D97706" style="background-color:#D97706;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;color:#23384B;mso-line-height-rule:exactly;">Reconnect your {{ provider_name }} connection</td></tr>
              <tr><td style="padding-bottom:14px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">{% if reason == 'AccountOfOffboardedUser' %}Your office's {{ provider_name }} connection uses the account <strong style="color:#23384B;">{{ account_email }}</strong>, which belongs to someone who's no longer with your office.{% elsif reason == 'ConnectedByOffboardedUser' %}Your office's {{ provider_name }} connection (<strong style="color:#23384B;">{{ account_email }}</strong>) was set up by someone who's no longer with your office.{% else %}Your office's {{ provider_name }} connection (<strong style="color:#23384B;">{{ account_email }}</strong>) is linked to someone who's no longer with your office.{% endif %}</td></tr>
              <tr><td style="padding-bottom:4px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Reconnect it with an account that belongs to your office, or disconnect it.</td></tr>
              <tr>
                <td align="left" style="padding:26px 0 18px 0;">
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ storage_url }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Open storage settings</a></td></tr></table>
                </td>
              </tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">You're receiving this because you manage your office's storage on {{ product_name }}.</td></tr>
            </table>
            """,
            Variables:
            [
                ("provider_name", VariableType.String, true, null, "Google Drive, OneDrive or Dropbox."),
                ("account_email", VariableType.String, true, null, "Account the connection uses."),
                ("reason", VariableType.String, true, null, "AccountOfOffboardedUser or ConnectedByOffboardedUser."),
                ("storage_url", VariableType.Url, true, null, "Storage page in the office CRM."),
                ProductName,
            ],
            ContentVersion: OffboardingNoticesVersion
        );

    /// <summary>Un buzón de oficina quedó ligado a un empleado retirado.</summary>
    private static NotificationTemplateSeed OfficeMailboxReviewRequired { get; } =
        new(
            EventKey: "connectors.office_mailbox_review_required.v1",
            TemplateKey: "connectors.office_mailbox_review_required.v1",
            Name: "Connectors — Office mailbox needs to be reconnected",
            Subject: "Reconnect the office mailbox {{ mailbox_email }}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Email</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#D97706" style="background-color:#D97706;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;color:#23384B;mso-line-height-rule:exactly;">Reconnect an office mailbox</td></tr>
              <tr><td style="padding-bottom:14px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">{% if reason == 'AddressOfOffboardedUser' %}The office mailbox <strong style="color:#23384B;">{{ mailbox_email }}</strong> is the address of someone who's no longer with your office.{% elsif reason == 'ConnectedByOffboardedUser' %}The office mailbox <strong style="color:#23384B;">{{ mailbox_email }}</strong> was connected by someone who's no longer with your office.{% else %}The office mailbox <strong style="color:#23384B;">{{ mailbox_email }}</strong> is linked to someone who's no longer with your office.{% endif %}</td></tr>
              <tr><td style="padding-bottom:4px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Reconnect it with an account your office controls, or disconnect it.</td></tr>
              <tr>
                <td align="left" style="padding:26px 0 18px 0;">
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ mailbox_url }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Open the mailbox</a></td></tr></table>
                </td>
              </tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">You're receiving this because you manage your office's mailboxes on {{ product_name }}.</td></tr>
            </table>
            """,
            Variables:
            [
                ("mailbox_email", VariableType.String, true, null, "Address of the office mailbox."),
                ("reason", VariableType.String, true, null, "AddressOfOffboardedUser or ConnectedByOffboardedUser."),
                ("mailbox_url", VariableType.Url, true, null, "The mailbox in the office CRM."),
                ProductName,
            ],
            ContentVersion: OffboardingNoticesVersion
        );

    private static NotificationTemplateSeed Invitation { get; } =
        new(
            EventKey: "auth.invitation_created.v1",
            TemplateKey: "auth.invitation",
            Name: "Auth — Invitación",
            Subject: "{% if account_kind == 'portal' %}{% if is_resend %}Reminder: activate{% else %}Activate{% endif %} your client portal at {{ office }}{% else %}{% if is_resend %}Reminder: your invitation to {{ office }} on {{ product_name }}{% else %}You've been invited to {{ office }} on {{ product_name }}{% endif %}{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Invitation</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">{% if account_kind == 'portal' %}Your client portal is ready{% else %}You've been invited to collaborate{% endif %}</td></tr>
              <tr><td style="padding-bottom:14px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">{% if account_kind == 'portal' %}<strong style="color:#1E466B;">{{ office }}</strong> invited you to your client portal on {{ product_name }}.{% else %}<strong style="color:#23384B;">{{ inviter }}</strong> invited you to join <strong style="color:#1E466B;">{{ office }}</strong> on {{ product_name }}.{% endif %}</td></tr>
              <tr><td style="padding-bottom:4px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">{% if account_kind == 'portal' %}Activate your account to see your documents, sign what your office sends you and message them securely.{% else %}Activate your account to access the workspace and start collaborating.{% endif %}</td></tr>
              <tr>
                <td align="left" style="padding:26px 0 22px 0;">
                  <!--[if mso]>
                  <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ invite_link }}" style="height:46px;v-text-anchor:middle;width:230px;" arcsize="22%" strokecolor="#1E466B" fillcolor="#1E466B"><w:anchorlock/><center style="color:#FFFFFF;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">Activate my account</center></v:roundrect>
                  <![endif]-->
                  <!--[if !mso]><!-- -->
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ invite_link }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Activate my account</a></td></tr></table>
                  <!--<![endif]-->
                </td>
              </tr>
              <tr><td style="padding-bottom:18px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF" style="background-color:#EAF4FF;border-radius:10px;"><tr><td style="padding:14px 18px;border-left:3px solid #67BAF4;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">Button not working? Copy this link into your browser:<br /><span style="word-break:break-all;color:#1E466B;">{{ invite_link }}</span></td></tr></table></td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">This link expires on {{ expires_at }} UTC. If you weren't expecting this invitation, you can ignore this email.</td></tr>
            </table>
            """,
            Variables:
            [
                ("office", VariableType.String, true, null, "Nombre del tenant o del producto si no hay tenant."),
                ("inviter", VariableType.String, true, null, "Nombre de quien invita."),
                ("invite_link", VariableType.Url, true, null, "URL de aceptación de la invitación."),
                ("expires_at", VariableType.String, true, null, "Fecha de expiración ya formateada (UTC)."),
                ("is_resend", VariableType.Bool, true, "false", "true si es un reenvío del mismo invite."),
                (
                    "product_name",
                    VariableType.String,
                    false,
                    "TaxOffix",
                    "Branding del producto (lo inyecta el renderer desde PlatformSettings)."
                ),
                (
                    "account_kind",
                    VariableType.String,
                    false,
                    "workspace",
                    "portal o workspace: la cuenta que crea la invitación."
                ),
            ],
            ContentVersion: 10
        );

    // Bloque de datos de la reunión compartido por las cuatro plantillas de meeting (F6): fecha local con
    // la zona de la oficina, duración, host y código. Cada fila se pinta solo si la variable viene.
    private const string MeetingDetailsRows = """
                  {% if start_local != blank %}<tr><td style="padding:2px 0;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#496174;mso-line-height-rule:exactly;"><strong style="color:#23384B;">When:</strong> {{ start_local }} ({{ time_zone }}){% if duration_minutes != blank %} &middot; {{ duration_minutes }} min{% endif %}</td></tr>{% endif %}
                  {% if host_name != blank %}<tr><td style="padding:2px 0;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#496174;mso-line-height-rule:exactly;"><strong style="color:#23384B;">Host:</strong> {{ host_name }}</td></tr>{% endif %}
                  {% if meeting_code != blank %}<tr><td style="padding:2px 0;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#496174;mso-line-height-rule:exactly;"><strong style="color:#23384B;">Meeting code:</strong> {{ meeting_code }}</td></tr>{% endif %}
        """;

    private static (string, VariableType, bool, string?, string?) ProductName =>
        (
            "product_name",
            VariableType.String,
            false,
            "TaxOffix",
            "Branding del producto (lo inyecta el renderer desde PlatformSettings)."
        );

    private static IReadOnlyList<(string, VariableType, bool, string?, string?)> MeetingDetailsVariables =>
        [
            ("meeting_title", VariableType.String, true, null, "Título de la reunión."),
            (
                "start_local",
                VariableType.String,
                false,
                null,
                "Hora de inicio ya formateada en la zona de la oficina (vacía si no hay fecha)."
            ),
            ("time_zone", VariableType.String, false, "UTC", "Zona IANA en la que está escrita start_local."),
            ("duration_minutes", VariableType.Number, false, null, "Duración en minutos, si la reunión la declaró."),
            ("host_name", VariableType.String, false, null, "Nombre del host de la reunión."),
            ("meeting_code", VariableType.String, false, null, "Código corto para entrar por código."),
        ];

    // v21 (F6): título, fecha local, duración, host, código, rama "in progress" y adjunto .ics.
    private static NotificationTemplateSeed MeetingInvitation { get; } =
        new(
            EventKey: "communication.meeting.invitation_created.v1",
            TemplateKey: "communication.meeting.invitation",
            Name: "Communication — Invitación a meeting",
            Subject: "{% if in_progress %}Join now: {{ meeting_title }}{% else %}Invitation: {{ meeting_title }}{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Meeting invitation</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">{% if in_progress %}A meeting is in progress{% else %}You've been invited to a meeting{% endif %}</td></tr>
              <tr><td style="padding-bottom:14px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ invitee_name }}</strong>, {% if in_progress %}you've been invited to join <strong style="color:#23384B;">{{ meeting_title }}</strong>, which is happening right now.{% else %}you've been invited to <strong style="color:#23384B;">{{ meeting_title }}</strong>.{% endif %}</td></tr>
              <tr><td style="padding-bottom:14px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF" style="background-color:#EAF4FF;border-radius:10px;"><tr><td style="padding:12px 18px;border-left:3px solid #67BAF4;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
            """
                + MeetingDetailsRows
                + """
                  </table></td></tr></table></td></tr>
                  <tr><td style="padding-bottom:4px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">{% if in_progress %}Use the button below to join now.{% else %}Use the button below to join when it's time.{% if start_local != blank %} A calendar invitation is attached so you can add it to your calendar.{% endif %}{% endif %}</td></tr>
                  <tr>
                    <td align="left" style="padding:26px 0 22px 0;">
                      <!--[if mso]>
                      <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ join_link }}" style="height:46px;v-text-anchor:middle;width:200px;" arcsize="22%" strokecolor="#1E466B" fillcolor="#1E466B"><w:anchorlock/><center style="color:#FFFFFF;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">{% if in_progress %}Join now{% else %}Join the meeting{% endif %}</center></v:roundrect>
                      <![endif]-->
                      <!--[if !mso]><!-- -->
                      <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ join_link }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">{% if in_progress %}Join now{% else %}Join the meeting{% endif %}</a></td></tr></table>
                      <!--<![endif]-->
                    </td>
                  </tr>
                  <tr><td style="padding-bottom:18px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF" style="background-color:#EAF4FF;border-radius:10px;"><tr><td style="padding:14px 18px;border-left:3px solid #67BAF4;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">Button not working? Copy this link into your browser:<br /><span style="word-break:break-all;color:#1E466B;">{{ join_link }}</span></td></tr></table></td></tr>
                """
                + RsvpButtonsRows
                + """
                  <tr><td style="padding-top:14px;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">This invitation expires on {{ expires_at }} UTC. If you weren't expecting it, you can ignore this email.</td></tr>
                </table>
                """,
            Variables:
            [
                (
                    "invitee_name",
                    VariableType.String,
                    false,
                    "there",
                    "Nombre del invitado (o 'there' si no se conoce)."
                ),
                (
                    "join_link",
                    VariableType.Url,
                    true,
                    null,
                    "URL para unirse al meeting (ya con el subdominio del tenant)."
                ),
                (
                    "expires_at",
                    VariableType.String,
                    true,
                    null,
                    "Fecha de expiración de la invitación ya formateada (UTC)."
                ),
                .. MeetingDetailsVariables,
                (
                    "in_progress",
                    VariableType.Bool,
                    false,
                    "false",
                    "La reunión ya está en curso: el correo invita a entrar ahora."
                ),
                .. RsvpButtonsVariables,
                ProductName,
            ],
            ContentVersion: RsvpButtonsVersion
        );

    private static NotificationTemplateSeed MeetingCancelled { get; } =
        new(
            EventKey: "communication.meeting.cancelled.v1",
            TemplateKey: "communication.meeting.cancelled.v1",
            Name: "Communication — Reunión cancelada",
            Subject: "Cancelled: {{ meeting_title }}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Meeting</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">The meeting was cancelled</td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;"><strong style="color:#23384B;">{{ meeting_title }}</strong>{% if start_local != blank %}, scheduled for {{ start_local }} ({{ time_zone }}),{% endif %} was cancelled{% if host_name != blank %} by {{ host_name }}{% endif %}.</td></tr>
              {% if reason != blank %}<tr><td style="padding-bottom:12px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#FDEEEE" style="background-color:#FDEEEE;border-radius:10px;"><tr><td style="padding:14px 18px;border-left:3px solid #D65B5B;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#23384B;mso-line-height-rule:exactly;">{{ reason }}</td></tr></table></td></tr>{% endif %}
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#496174;mso-line-height-rule:exactly;">No action is needed.{% if start_local != blank %} The attached calendar update removes it from your calendar.{% endif %}</td></tr>
            </table>
            """,
            Variables:
            [
                ("meeting_title", VariableType.String, true, null, "Título de la reunión."),
                (
                    "start_local",
                    VariableType.String,
                    false,
                    null,
                    "Hora que tenía la reunión, en la zona de la oficina."
                ),
                ("time_zone", VariableType.String, false, "UTC", "Zona IANA de start_local."),
                ("host_name", VariableType.String, false, null, "Nombre del host de la reunión."),
                ("reason", VariableType.String, false, null, "Motivo, si el host lo escribió."),
                ProductName,
            ],
            ContentVersion: 1
        );

    private static NotificationTemplateSeed MeetingRescheduled { get; } =
        new(
            EventKey: "communication.meeting.rescheduled.v1",
            TemplateKey: "communication.meeting.rescheduled.v1",
            Name: "Communication — Reunión reprogramada",
            Subject: "Rescheduled: {{ meeting_title }}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Meeting</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">The meeting was rescheduled</td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;"><strong style="color:#23384B;">{{ meeting_title }}</strong> has a new time{% if host_name != blank %}, set by {{ host_name }}{% endif %}.</td></tr>
              <tr><td style="padding-bottom:12px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF" style="background-color:#EAF4FF;border-radius:10px;"><tr><td style="padding:12px 18px;border-left:3px solid #67BAF4;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              {% if previous_local != blank %}<tr><td style="padding:2px 0;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#70869A;mso-line-height-rule:exactly;"><strong>Previously:</strong> <span style="text-decoration:line-through;">{{ previous_local }}</span> ({{ time_zone }})</td></tr>{% endif %}
              {% if new_local != blank %}<tr><td style="padding:2px 0;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#496174;mso-line-height-rule:exactly;"><strong style="color:#23384B;">New time:</strong> {{ new_local }} ({{ time_zone }}){% if duration_minutes != blank %} &middot; {{ duration_minutes }} min{% endif %}</td></tr>{% else %}<tr><td style="padding:2px 0;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#496174;mso-line-height-rule:exactly;">The new time has not been set yet.</td></tr>{% endif %}
              {% if meeting_code != blank %}<tr><td style="padding:2px 0;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#496174;mso-line-height-rule:exactly;"><strong style="color:#23384B;">Meeting code:</strong> {{ meeting_code }}</td></tr>{% endif %}
              </table></td></tr></table></td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#496174;mso-line-height-rule:exactly;">Your invitation link still works.{% if new_local != blank %} The attached calendar update moves it in your calendar.{% endif %}</td></tr>
            </table>
            """,
            Variables:
            [
                ("meeting_title", VariableType.String, true, null, "Título de la reunión."),
                ("previous_local", VariableType.String, false, null, "Hora anterior, en la zona de la oficina."),
                (
                    "new_local",
                    VariableType.String,
                    false,
                    null,
                    "Hora nueva, en la zona de la oficina (vacía si aún no hay)."
                ),
                ("time_zone", VariableType.String, false, "UTC", "Zona IANA de las horas."),
                (
                    "duration_minutes",
                    VariableType.Number,
                    false,
                    null,
                    "Duración en minutos, si la reunión la declaró."
                ),
                ("host_name", VariableType.String, false, null, "Nombre del host de la reunión."),
                ("meeting_code", VariableType.String, false, null, "Código corto para entrar por código."),
                ProductName,
            ],
            ContentVersion: 1
        );

    private static NotificationTemplateSeed MeetingReminder { get; } =
        new(
            EventKey: "communication.meeting.reminder.v1",
            TemplateKey: "communication.meeting.reminder.v1",
            Name: "Communication — Recordatorio de reunión",
            Subject: "Starting in {{ minutes_before }} min: {{ meeting_title }}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Meeting reminder</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Your meeting starts in {{ minutes_before }} minutes</td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;"><strong style="color:#23384B;">{{ meeting_title }}</strong> is about to start.</td></tr>
              <tr><td style="padding-bottom:14px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF" style="background-color:#EAF4FF;border-radius:10px;"><tr><td style="padding:12px 18px;border-left:3px solid #67BAF4;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
            """
                + MeetingDetailsRows
                + """
                  </table></td></tr></table></td></tr>
                  <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#496174;mso-line-height-rule:exactly;">Use the link from your invitation email to join{% if meeting_code != blank %}, or enter the meeting code{% endif %}.</td></tr>
                </table>
                """,
            Variables:
            [
                .. MeetingDetailsVariables,
                ("minutes_before", VariableType.Number, true, null, "Minutos que faltan para la reunión."),
                ProductName,
            ],
            ContentVersion: 1
        );

    private static NotificationTemplateSeed PasswordReset { get; } =
        new(
            EventKey: "auth.password_reset_requested.v1",
            TemplateKey: "auth.password_reset",
            Name: "Auth — Restablecer contraseña",
            Subject: "Reset your {% if account_kind == 'portal' %}client portal{% else %}{{ product_name }}{% endif %} password",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Security</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Reset your password</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">We received a request to reset the password for your {% if account_kind == 'portal' %}client portal account at <strong style="color:#23384B;">{{ office }}</strong>{% else %}<strong style="color:#23384B;">{{ office }}</strong> workspace account{% endif %}. Only this account changes: any other {{ product_name }} account you have keeps its password.</td></tr>
              <tr>
                <td align="left" style="padding:26px 0 22px 0;">
                  <!--[if mso]>
                  <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ reset_link }}" style="height:46px;v-text-anchor:middle;width:220px;" arcsize="22%" strokecolor="#1E466B" fillcolor="#1E466B"><w:anchorlock/><center style="color:#FFFFFF;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">Reset password</center></v:roundrect>
                  <![endif]-->
                  <!--[if !mso]><!-- -->
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ reset_link }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Reset password</a></td></tr></table>
                  <!--<![endif]-->
                </td>
              </tr>
              <tr><td style="padding-bottom:18px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF" style="background-color:#EAF4FF;border-radius:10px;"><tr><td style="padding:14px 18px;border-left:3px solid #67BAF4;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">Button not working? Copy this link into your browser:<br /><span style="word-break:break-all;color:#1E466B;">{{ reset_link }}</span></td></tr></table></td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">This link expires on {{ expires_at }} UTC. If you didn't request this, ignore this email: your current password stays valid.</td></tr>
            </table>
            """,
            Variables:
            [
                ("reset_link", VariableType.Url, true, null, "URL de restablecimiento de contraseña."),
                ("expires_at", VariableType.String, true, null, "Fecha de expiración ya formateada (UTC)."),
                (
                    "product_name",
                    VariableType.String,
                    false,
                    "TaxOffix",
                    "Branding del producto (lo inyecta el renderer desde PlatformSettings)."
                ),
                (
                    "account_kind",
                    VariableType.String,
                    false,
                    "workspace",
                    "portal o workspace: de qué cuenta es el reset."
                ),
                ("office", VariableType.String, false, "your office", "Nombre de la oficina de esa cuenta."),
            ],
            ContentVersion: 10
        );

    private static NotificationTemplateSeed OtpCode { get; } =
        new(
            EventKey: "auth.mfa_otp_requested.v1",
            TemplateKey: "auth.otp_code",
            Name: "Auth — Código OTP",
            Subject: "{{ code }} is your {{ product_name }} code",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Verification</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Your verification code</td></tr>
              <tr><td style="padding-bottom:4px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Your verification code for <strong style="color:#23384B;">{{ reason }}</strong> is:</td></tr>
              <tr><td style="padding:14px 0 18px 0;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF" style="background-color:#EAF4FF;border-radius:12px;"><tr><td align="center" style="padding:22px 16px 6px 16px;font-family:Arial,Helvetica,sans-serif;font-size:34px;line-height:40px;letter-spacing:10px;font-weight:bold;color:#1E466B;mso-line-height-rule:exactly;">{{ code }}</td></tr><tr><td align="center" style="padding:0 16px 18px 16px;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:18px;color:#70869A;mso-line-height-rule:exactly;">Valid for a few minutes</td></tr></table></td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">Never share this code. The {{ product_name }} team will never ask you for it by phone, chat, or email.</td></tr>
            </table>
            """,
            Variables:
            [
                ("code", VariableType.String, true, null, "Código OTP de un solo uso."),
                ("reason", VariableType.String, true, null, "Motivo en texto ya traducido (p. ej. 'sign in')."),
                (
                    "product_name",
                    VariableType.String,
                    false,
                    "TaxOffix",
                    "Branding del producto (lo inyecta el renderer desde PlatformSettings)."
                ),
            ]
        );

    private static NotificationTemplateSeed EmailChange { get; } =
        new(
            EventKey: "auth.email_change_requested.v1",
            TemplateKey: "auth.email_change",
            Name: "Auth — Confirmar cambio de email",
            Subject: "Confirm your new email on {{ product_name }}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Security</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Confirm your new email</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">You requested to change the email of your {% if account_kind == 'portal' %}client portal account at <strong style="color:#23384B;">{{ office }}</strong>{% else %}<strong style="color:#23384B;">{{ office }}</strong> workspace account{% endif %}. Confirm the new address to activate it:</td></tr>
              <tr>
                <td align="left" style="padding:26px 0 22px 0;">
                  <!--[if mso]>
                  <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ confirm_link }}" style="height:46px;v-text-anchor:middle;width:220px;" arcsize="22%" strokecolor="#1E466B" fillcolor="#1E466B"><w:anchorlock/><center style="color:#FFFFFF;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">Confirm new email</center></v:roundrect>
                  <![endif]-->
                  <!--[if !mso]><!-- -->
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ confirm_link }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Confirm new email</a></td></tr></table>
                  <!--<![endif]-->
                </td>
              </tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">This link expires on {{ expires_at }} UTC. If you didn't request this change, contact your office administrator.</td></tr>
            </table>
            """,
            Variables:
            [
                ("confirm_link", VariableType.Url, true, null, "URL de confirmación del nuevo email."),
                ("expires_at", VariableType.String, true, null, "Fecha de expiración ya formateada (UTC)."),
                (
                    "product_name",
                    VariableType.String,
                    false,
                    "TaxOffix",
                    "Branding del producto (lo inyecta el renderer desde PlatformSettings)."
                ),
                (
                    "account_kind",
                    VariableType.String,
                    false,
                    "workspace",
                    "portal o workspace: qué cuenta cambia de email."
                ),
                ("office", VariableType.String, false, "your office", "Nombre de la oficina de esa cuenta."),
            ],
            ContentVersion: 10
        );

    private static NotificationTemplateSeed SecurityAlert { get; } =
        new(
            EventKey: "auth.email_change_security_alert.v1",
            TemplateKey: "auth.security_alert",
            Name: "Auth — Alerta de seguridad",
            Subject: "Security alert on your {{ product_name }} account",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Security</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Security alert</td></tr>
              <tr><td style="padding-bottom:18px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#FDEEEE" style="background-color:#FDEEEE;border-radius:10px;"><tr><td style="padding:14px 18px;border-left:3px solid #D65B5B;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#23384B;mso-line-height-rule:exactly;">{{ description }}{% if ip_address != blank %}<br />IP address: <strong>{{ ip_address }}</strong>.{% endif %}</td></tr></table></td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">If you recognize this activity you can ignore this email. If it wasn't you, change your password immediately and contact your administrator.</td></tr>
            </table>
            """,
            Variables:
            [
                ("description", VariableType.String, true, null, "Descripción del evento de seguridad."),
                ("ip_address", VariableType.String, false, null, "IP de origen, si está disponible."),
                (
                    "product_name",
                    VariableType.String,
                    false,
                    "TaxOffix",
                    "Branding del producto (lo inyecta el renderer desde PlatformSettings)."
                ),
            ]
        );

    private static NotificationTemplateSeed TenantRecovery { get; } =
        new(
            EventKey: "auth.tenant_recovery_requested.v1",
            TemplateKey: "auth.tenant_recovery",
            Name: "Auth — Encuentra tu oficina",
            Subject: "Your offices on {{ product_name }}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Account</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Your offices</td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">We found the following offices linked to your email:</td></tr>
              {% for office in offices %}
              <tr><td style="padding-bottom:8px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF" style="background-color:#EAF4FF;border-radius:10px;"><tr><td style="padding:12px 18px;border-left:3px solid #67BAF4;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#23384B;mso-line-height-rule:exactly;"><a href="{{ office.url }}" style="color:#1E466B;text-decoration:none;font-weight:bold;">{{ office.name }}</a><br /><span style="font-size:12px;color:#70869A;word-break:break-all;">{{ office.url }}</span></td></tr></table></td></tr>
              {% endfor %}
              <tr><td style="padding-top:6px;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">If you don't recognize this request, you can ignore this email.</td></tr>
            </table>
            """,
            Variables:
            [
                ("offices", VariableType.String, true, null, "Lista de objetos { name, url } por oficina."),
                (
                    "product_name",
                    VariableType.String,
                    false,
                    "TaxOffix",
                    "Branding del producto (lo inyecta el renderer desde PlatformSettings)."
                ),
            ]
        );

    private static NotificationTemplateSeed Welcome { get; } =
        new(
            EventKey: "auth.user_registered.v1",
            TemplateKey: "auth.welcome",
            Name: "Auth — Bienvenida",
            Subject: "Welcome to {{ product_name }}!",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Welcome</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Welcome aboard!</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ name }}</strong>, your account is ready. Sign in and get started.</td></tr>
              <tr>
                <td align="left" style="padding:26px 0 4px 0;">
                  <!--[if mso]>
                  <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ portal_link }}" style="height:46px;v-text-anchor:middle;width:200px;" arcsize="22%" strokecolor="#1E466B" fillcolor="#1E466B"><w:anchorlock/><center style="color:#FFFFFF;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">Go to {{ product_name }}</center></v:roundrect>
                  <![endif]-->
                  <!--[if !mso]><!-- -->
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ portal_link }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Go to {{ product_name }}</a></td></tr></table>
                  <!--<![endif]-->
                </td>
              </tr>
            </table>
            """,
            Variables:
            [
                ("name", VariableType.String, true, null, "Nombre del usuario."),
                ("portal_link", VariableType.Url, true, null, "URL base del portal."),
                (
                    "product_name",
                    VariableType.String,
                    false,
                    "TaxOffix",
                    "Branding del producto (lo inyecta el renderer desde PlatformSettings)."
                ),
            ]
        );

    private static NotificationTemplateSeed SignatureInvitation { get; } =
        new(
            EventKey: "sig.signer_invited.v1",
            TemplateKey: "sig.invitation.v1",
            Name: "Signature — Invitación a firmar",
            Subject: "{% if language == 'Es' %}{{ product_name }} — Solicitud de firma pendiente{% else %}{{ product_name }} — Signature request pending{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">{% if language == 'Es' %}Firma{% else %}Signature{% endif %}</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              {% if language == 'Es' %}
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Tienes una firma pendiente</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hola <strong style="color:#23384B;">{{ full_name }}</strong>, tienes una solicitud de firma pendiente en {{ product_name }}.{% if requires_consent %} Se te pedirá aceptar el consentimiento antes de firmar.{% endif %}</td></tr>
              {% else %}
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">You have a pending signature</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ full_name }}</strong>, you have a pending signature request on {{ product_name }}.{% if requires_consent %} You'll be asked to accept the consent before signing.{% endif %}</td></tr>
              {% endif %}
              <tr>
                <td align="left" style="padding:26px 0 22px 0;">
                  <!--[if mso]>
                  <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ invite_link }}" style="height:46px;v-text-anchor:middle;width:240px;" arcsize="22%" strokecolor="#1E466B" fillcolor="#1E466B"><w:anchorlock/><center style="color:#FFFFFF;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">{% if language == 'Es' %}Abrir solicitud de firma{% else %}Open signature request{% endif %}</center></v:roundrect>
                  <![endif]-->
                  <!--[if !mso]><!-- -->
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ invite_link }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">{% if language == 'Es' %}Abrir solicitud de firma{% else %}Open signature request{% endif %}</a></td></tr></table>
                  <!--<![endif]-->
                </td>
              </tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">{% if expires_at %}{% if language == 'Es' %}El enlace vence el {{ expires_at }} UTC.{% else %}The link expires on {{ expires_at }} UTC.{% endif %}{% else %}{% if language == 'Es' %}Este enlace no vence.{% else %}This link does not expire.{% endif %}{% endif %}</td></tr>
            </table>
            """,
            Variables:
            [
                ("full_name", VariableType.String, true, null, "Nombre completo del firmante."),
                ("invite_link", VariableType.Url, true, null, "URL pública de firma."),
                (
                    "expires_at",
                    VariableType.String,
                    false,
                    null,
                    "Fecha de expiración ya formateada (UTC); vacío si la request no expira."
                ),
                ("requires_consent", VariableType.Bool, true, "false", "true si requiere aceptar consent antes."),
                ("language", VariableType.String, true, "En", "'Es' o 'En'."),
            ]
        );

    private static NotificationTemplateSeed SharedFileInvited { get; } =
        new(
            EventKey: "storage.share_invited.v1",
            TemplateKey: "storage.share_invited.v1",
            Name: "Documents — Documento compartido con externo",
            Subject: "{% if language == 'Es' %}{{ product_name }} — Te compartieron un documento{% else %}{{ product_name }} — A document was shared with you{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">{% if language == 'Es' %}Documento{% else %}Document{% endif %}</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              {% if language == 'Es' %}
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Te compartieron un documento</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Se compartió el documento <strong style="color:#23384B;">{{ file_name }}</strong> contigo. Ábrelo de forma segura con el botón de abajo.</td></tr>
              {% else %}
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">A document was shared with you</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">The document <strong style="color:#23384B;">{{ file_name }}</strong> was shared with you. Open it securely with the button below.</td></tr>
              {% endif %}
              <tr>
                <td align="left" style="padding:26px 0 22px 0;">
                  <!--[if mso]>
                  <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ open_link }}" style="height:46px;v-text-anchor:middle;width:220px;" arcsize="22%" strokecolor="#1E466B" fillcolor="#1E466B"><w:anchorlock/><center style="color:#FFFFFF;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">{% if language == 'Es' %}Abrir documento{% else %}Open document{% endif %}</center></v:roundrect>
                  <![endif]-->
                  <!--[if !mso]><!-- -->
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ open_link }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">{% if language == 'Es' %}Abrir documento{% else %}Open document{% endif %}</a></td></tr></table>
                  <!--<![endif]-->
                </td>
              </tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">{% if language == 'Es' %}El enlace vence el {{ expires_at }} UTC y es solo para ti.{% else %}The link expires on {{ expires_at }} UTC and is meant only for you.{% endif %}</td></tr>
            </table>
            """,
            Variables:
            [
                ("file_name", VariableType.String, true, null, "Nombre del archivo compartido."),
                ("open_link", VariableType.Url, true, null, "URL de la página pública branded (/s/{token}?email=)."),
                ("expires_at", VariableType.String, true, null, "Fecha de expiración ya formateada (UTC)."),
                ("permission", VariableType.String, true, "Download", "'View' o 'Download'."),
                ("language", VariableType.String, true, "En", "'Es' o 'En'."),
            ]
        );

    private static NotificationTemplateSeed SignatureReminder { get; } =
        new(
            EventKey: "sig.request_reminder_due.v1",
            TemplateKey: "sig.reminder.v1",
            Name: "Signature — Recordatorio de firma",
            Subject: "{% if language == 'Es' %}{{ product_name }} — Recordatorio: tu firma sigue pendiente{% else %}{{ product_name }} — Reminder: your signature is still pending{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">{% if language == 'Es' %}Firma{% else %}Signature{% endif %}</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              {% if language == 'Es' %}
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Tu firma sigue pendiente</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hola <strong style="color:#23384B;">{{ full_name }}</strong>, este es un recordatorio ({{ reminders_sent }} de 3) de que tu firma sigue pendiente en {{ product_name }}. El enlace vence el {{ expires_at }} UTC.</td></tr>
              {% else %}
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Your signature is still pending</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ full_name }}</strong>, this is reminder {{ reminders_sent }} of 3 that your signature is still pending on {{ product_name }}. The link expires on {{ expires_at }} UTC.</td></tr>
              {% endif %}
              <tr>
                <td align="left" style="padding:26px 0 4px 0;">
                  <!--[if mso]>
                  <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ invite_link }}" style="height:46px;v-text-anchor:middle;width:240px;" arcsize="22%" strokecolor="#1E466B" fillcolor="#1E466B"><w:anchorlock/><center style="color:#FFFFFF;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">{% if language == 'Es' %}Abrir solicitud de firma{% else %}Open signature request{% endif %}</center></v:roundrect>
                  <![endif]-->
                  <!--[if !mso]><!-- -->
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ invite_link }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">{% if language == 'Es' %}Abrir solicitud de firma{% else %}Open signature request{% endif %}</a></td></tr></table>
                  <!--<![endif]-->
                </td>
              </tr>
            </table>
            """,
            Variables:
            [
                ("full_name", VariableType.String, true, null, "Nombre completo del firmante."),
                ("invite_link", VariableType.Url, true, null, "URL pública de firma."),
                ("expires_at", VariableType.String, true, null, "Fecha de expiración ya formateada (UTC)."),
                ("reminders_sent", VariableType.Number, true, null, "Número de recordatorio (1..3)."),
                ("language", VariableType.String, true, "En", "'Es' o 'En'."),
            ]
        );

    private static NotificationTemplateSeed SignatureCompleted { get; } =
        new(
            EventKey: "sig.request_completed.v1",
            TemplateKey: "sig.completed.v1",
            Name: "Signature — Firma completada",
            Subject: "{% if language == 'Es' %}{{ product_name }} — Firma completada{% else %}{{ product_name }} — Signature completed{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">{% if language == 'Es' %}Firma{% else %}Signature{% endif %}</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              {% if language == 'Es' %}
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Firma completada</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hola <strong style="color:#23384B;">{{ full_name }}</strong>, {% if is_reissue %}este es tu nuevo enlace para descargar los documentos firmados; el anterior ya no funciona.{% else %}el proceso de firma se completó exitosamente el {{ completed_at }} UTC. Puedes descargar los documentos firmados aquí abajo.{% endif %}</td></tr>
              {% else %}
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Signature completed</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ full_name }}</strong>, {% if is_reissue %}here is your new link to download the signed documents; the previous one no longer works.{% else %}the signature process was completed successfully on {{ completed_at }} UTC. You can download the signed documents below.{% endif %}</td></tr>
              {% endif %}
              {% if download_links and download_links.size > 0 %}
              <tr><td style="padding-top:18px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:22px;color:#496174;">{% if language == 'Es' %}Documentos disponibles ({{ document_count }}):{% else %}Available documents ({{ document_count }}):{% endif %}</td></tr>
              {% for document in download_links %}
              <tr>
                <td align="left" style="padding-top:10px;">
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ document.url }}" target="_blank" style="display:inline-block;padding:12px 24px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">{{ document.title }}</a></td></tr></table>
                </td>
              </tr>
              {% endfor %}
              {% endif %}
              {% if delivery_url %}
              <tr><td style="padding-top:20px;font-family:Arial,Helvetica,sans-serif;font-size:13px;line-height:20px;color:#70869A;mso-line-height-rule:exactly;">{% if language == 'Es' %}Este enlace es personal y vale hasta el {{ expires_at }}.{% if can_request_new_link %} Si vence, ábrelo igual y podrás pedir uno nuevo, que llegará a este mismo correo.{% else %} Si vence, pide uno nuevo a la oficina que te lo envió.{% endif %}{% if has_attachments %} También te adjuntamos los archivos.{% endif %}{% else %}This link is personal and valid until {{ expires_at }}.{% if can_request_new_link %} If it expires, open it anyway and you can request a new one, sent to this same email.{% else %} If it expires, ask the office that sent it for a new one.{% endif %}{% if has_attachments %} The files are also attached.{% endif %}{% endif %}</td></tr>
              {% endif %}
              {% if portal_url %}
              <tr><td style="padding-top:10px;font-family:Arial,Helvetica,sans-serif;font-size:13px;line-height:20px;color:#70869A;mso-line-height-rule:exactly;">{% if language == 'Es' %}También están en tu portal de cliente: <a href="{{ portal_url }}" target="_blank" style="color:#1E466B;font-weight:bold;text-decoration:underline;">abrir el portal</a>.{% else %}They're also in your client portal: <a href="{{ portal_url }}" target="_blank" style="color:#1E466B;font-weight:bold;text-decoration:underline;">open the portal</a>.{% endif %}</td></tr>
              {% endif %}
            </table>
            """,
            Variables:
            [
                ("full_name", VariableType.String, true, null, "Nombre completo del firmante."),
                ("completed_at", VariableType.String, true, null, "Fecha de finalización ya formateada (UTC)."),
                ("download_links", VariableType.String, false, null, "Lista de objetos { title, url } por documento."),
                ("document_count", VariableType.Number, true, "1", "Cantidad de documentos sellados."),
                ("language", VariableType.String, true, "En", "'Es' o 'En'."),
                (
                    "delivery_url",
                    VariableType.Url,
                    false,
                    null,
                    "Página de entrega de Signature con todos los archivos."
                ),
                ("expires_at", VariableType.String, false, null, "Fecha (UTC) hasta la que vale el enlace."),
                (
                    "can_request_new_link",
                    VariableType.Bool,
                    false,
                    "false",
                    "Si al vencer el firmante puede pedir un enlace nuevo."
                ),
                (
                    "portal_url",
                    VariableType.Url,
                    false,
                    null,
                    "Carpeta de documentos del portal (solo clientes con cuenta)."
                ),
                ("is_reissue", VariableType.Bool, false, "false", "Es un enlace nuevo que reemplaza al anterior."),
                ("has_attachments", VariableType.Bool, false, "false", "Los archivos también van adjuntos."),
            ],
            ContentVersion: 16
        );

    // F7 — copia inmediata: cada firmante recibe lo que firmó cuando firma él, aunque los demás
    // todavía no hayan terminado. Subject y copy dejan claro que es "in progress".
    private static NotificationTemplateSeed SignaturePartialCopy { get; } =
        new(
            EventKey: "sig.partial_copy_ready.v1",
            TemplateKey: "sig.partial_copy.v1",
            Name: "Signature — Copia inmediata al firmar",
            Subject: "{% if language == 'Es' %}Tu copia firmada{% else %}Your signed copy{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">{% if language == 'Es' %}Firma{% else %}Signature{% endif %}</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              {% if language == 'Es' %}
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Tu copia de lo que firmaste</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hola <strong style="color:#23384B;">{{ full_name }}</strong>, gracias por completar {{ document_count }} documento(s) el {{ signed_at }} UTC.</td></tr>
              {% if total_signers and total_signers > 1 %}
              {% if send_sealed_to_signers %}
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">{% if has_attachments %}Adjuntamos{% else %}Aquí tienes{% endif %} tu copia con las firmas recogidas hasta ahora. <em>Todavía faltan firmantes</em>; cuando el documento quede completo, recibirás el PDF final sellado.</td></tr>
              {% else %}
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">{% if has_attachments %}Adjuntamos{% else %}Aquí tienes{% endif %} tu copia con las firmas recogidas hasta ahora. <em>Todavía faltan firmantes</em>. Guárdala como constancia de tu firma.</td></tr>
              {% endif %}
              {% else %}
              {% if send_sealed_to_signers %}
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Esta es tu copia con tu firma estampada. En breve recibirás el PDF final sellado con los sellos legales.</td></tr>
              {% else %}
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Esta es tu copia con tu firma estampada. Guárdala como constancia de tu firma.</td></tr>
              {% endif %}
              {% endif %}
              {% else %}
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Your copy of what you signed</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ full_name }}</strong>, thanks for completing {{ document_count }} document(s) on {{ signed_at }} UTC.</td></tr>
              {% if total_signers and total_signers > 1 %}
              {% if send_sealed_to_signers %}
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">{% if has_attachments %}Attached is{% else %}Here is{% endif %} your copy with the signatures collected so far. <em>Other signers haven't finished yet</em>; when the document is complete, you'll receive the final sealed PDF.</td></tr>
              {% else %}
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">{% if has_attachments %}Attached is{% else %}Here is{% endif %} your copy with the signatures collected so far. <em>Other signers haven't finished yet</em>. Keep it as a record of your signature.</td></tr>
              {% endif %}
              {% else %}
              {% if send_sealed_to_signers %}
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">This is your copy with your signature stamped. The final sealed PDF with legal evidence will arrive shortly.</td></tr>
              {% else %}
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">This is your copy with your signature stamped. Keep it as a record of your signature.</td></tr>
              {% endif %}
              {% endif %}
              {% endif %}
              {% if download_links and download_links.size > 0 %}
              {% for item in download_links %}
              <tr>
                <td align="left" style="padding:22px 0 4px 0;">
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ item.url }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">{{ item.title }}</a></td></tr></table>
                </td>
              </tr>
              {% endfor %}
              {% endif %}
              {% if delivery_url %}
              <tr><td style="padding-top:20px;font-family:Arial,Helvetica,sans-serif;font-size:13px;line-height:20px;color:#70869A;mso-line-height-rule:exactly;">{% if language == 'Es' %}Este enlace es personal y vale hasta el {{ expires_at }}.{% if can_request_new_link %} Si vence, ábrelo igual y podrás pedir uno nuevo, que llegará a este mismo correo.{% else %} Si vence, pide uno nuevo a la oficina que te lo envió.{% endif %}{% if has_attachments %} También te adjuntamos los archivos.{% endif %}{% else %}This link is personal and valid until {{ expires_at }}.{% if can_request_new_link %} If it expires, open it anyway and you can request a new one, sent to this same email.{% else %} If it expires, ask the office that sent it for a new one.{% endif %}{% if has_attachments %} The files are also attached.{% endif %}{% endif %}</td></tr>
              {% endif %}
              {% if portal_url %}
              <tr><td style="padding-top:10px;font-family:Arial,Helvetica,sans-serif;font-size:13px;line-height:20px;color:#70869A;mso-line-height-rule:exactly;">{% if language == 'Es' %}También están en tu portal de cliente: <a href="{{ portal_url }}" target="_blank" style="color:#1E466B;font-weight:bold;text-decoration:underline;">abrir el portal</a>.{% else %}They're also in your client portal: <a href="{{ portal_url }}" target="_blank" style="color:#1E466B;font-weight:bold;text-decoration:underline;">open the portal</a>.{% endif %}</td></tr>
              {% endif %}
            </table>
            """,
            Variables:
            [
                ("full_name", VariableType.String, true, null, "Nombre del firmante destinatario."),
                ("signed_at", VariableType.String, true, null, "Fecha de firma del destinatario (UTC)."),
                ("download_links", VariableType.String, false, null, "Lista de objetos { title, url } por documento."),
                ("document_count", VariableType.Number, true, "1", "Cantidad de documentos firmados."),
                ("language", VariableType.String, true, "En", "'Es' o 'En'."),
                ("total_signers", VariableType.Number, false, "1", "Cantidad total de firmantes del request."),
                (
                    "send_sealed_to_signers",
                    VariableType.Bool,
                    false,
                    "false",
                    "True si el preparador va a enviar el PDF sellado al final."
                ),
                (
                    "delivery_url",
                    VariableType.Url,
                    false,
                    null,
                    "Página de entrega de Signature con todos los archivos."
                ),
                ("expires_at", VariableType.String, false, null, "Fecha (UTC) hasta la que vale el enlace."),
                (
                    "can_request_new_link",
                    VariableType.Bool,
                    false,
                    "false",
                    "Si al vencer el firmante puede pedir un enlace nuevo."
                ),
                (
                    "portal_url",
                    VariableType.Url,
                    false,
                    null,
                    "Carpeta de documentos del portal (solo clientes con cuenta)."
                ),
                ("is_reissue", VariableType.Bool, false, "false", "Es un enlace nuevo que reemplaza al anterior."),
                ("has_attachments", VariableType.Bool, false, "false", "Los archivos también van adjuntos."),
            ],
            ContentVersion: 18
        );

    private static NotificationTemplateSeed SignatureCertificateReady { get; } =
        new(
            EventKey: "sig.certificate_ready.v1",
            TemplateKey: "sig.certificate.v1",
            Name: "Signature — Certificado listo",
            Subject: "{% if language == 'Es' %}{{ product_name }} — Tu certificado de firma{% if document_title %}: {{ document_title }}{% endif %}{% else %}{{ product_name }} — Your signature certificate{% if document_title %}: {{ document_title }}{% endif %}{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">{% if language == 'Es' %}Firma{% else %}Signature{% endif %}</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              {% if language == 'Es' %}
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Tu certificado de firma</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hola <strong style="color:#23384B;">{{ full_name }}</strong>, el certificado de finalización{% if document_title %} de <strong style="color:#23384B;">{{ document_title }}</strong>{% endif %} del {{ completed_at }} UTC ya está disponible.</td></tr>
              {% else %}
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Your signature certificate</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ full_name }}</strong>, the certificate of completion{% if document_title %} for <strong style="color:#23384B;">{{ document_title }}</strong>{% endif %} from {{ completed_at }} UTC is now available.</td></tr>
              {% endif %}
              {% if download_link %}
              <tr>
                <td align="left" style="padding:22px 0 4px 0;">
                  <!--[if mso]>
                  <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ download_link }}" style="height:46px;v-text-anchor:middle;width:280px;" arcsize="22%" strokecolor="#1E466B" fillcolor="#1E466B"><w:anchorlock/><center style="color:#FFFFFF;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">{% if language == 'Es' %}Descargar certificado{% else %}Download certificate{% endif %}</center></v:roundrect>
                  <![endif]-->
                  <!--[if !mso]><!-- -->
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ download_link }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">{% if language == 'Es' %}Descargar certificado{% else %}Download certificate{% endif %}</a></td></tr></table>
                  <!--<![endif]-->
                </td>
              </tr>
              {% endif %}
            </table>
            """,
            Variables:
            [
                ("full_name", VariableType.String, true, null, "Nombre completo del firmante."),
                ("completed_at", VariableType.String, true, null, "Fecha de finalización ya formateada (UTC)."),
                ("download_link", VariableType.Url, false, null, "URL pública de descarga del certificado (opcional)."),
                (
                    "document_title",
                    VariableType.String,
                    false,
                    null,
                    "Documento cuando se genera un certificado por documento."
                ),
                ("language", VariableType.String, true, "En", "'Es' o 'En'."),
            ],
            ContentVersion: 18
        );

    private static NotificationTemplateSeed SignatureExpired { get; } =
        new(
            EventKey: "sig.request_expired.v1",
            TemplateKey: "sig.expired.v1",
            Name: "Signature — Solicitud expirada",
            Subject: "{% if language == 'Es' %}{{ product_name }} — Solicitud de firma expirada{% else %}{{ product_name }} — Signature request expired{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">{% if language == 'Es' %}Firma{% else %}Signature{% endif %}</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              {% if language == 'Es' %}
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Solicitud de firma expirada</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hola <strong style="color:#23384B;">{{ full_name }}</strong>, la solicitud de firma venció el {{ expired_at }} UTC sin completarse. Si todavía necesitas firmar, contacta a quien te la envió para que genere una nueva solicitud.</td></tr>
              {% else %}
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Signature request expired</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ full_name }}</strong>, the signature request expired on {{ expired_at }} UTC without being completed. If you still need to sign, contact the sender to issue a new request.</td></tr>
              {% endif %}
            </table>
            """,
            Variables:
            [
                ("full_name", VariableType.String, true, null, "Nombre completo del firmante."),
                ("expired_at", VariableType.String, true, null, "Fecha de expiración ya formateada (UTC)."),
                ("language", VariableType.String, true, "En", "'Es' o 'En'."),
            ]
        );

    private static NotificationTemplateSeed SignatureDeclined { get; } =
        new(
            EventKey: "sig.signer_rejected.v1",
            TemplateKey: "sig.declined.v1",
            Name: "Signature — Solicitud cancelada",
            Subject: "{% if language == 'Es' %}{{ product_name }} — Solicitud de firma cancelada{% else %}{{ product_name }} — Signature request cancelled{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">{% if language == 'Es' %}Firma{% else %}Signature{% endif %}</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              {% if language == 'Es' %}
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Solicitud de firma cancelada</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hola <strong style="color:#23384B;">{{ full_name }}</strong>, uno de los firmantes rechazó firmar el documento, por lo que la solicitud fue cancelada. No necesitas hacer nada más.</td></tr>
              {% else %}
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Signature request cancelled</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ full_name }}</strong>, one of the signers declined to sign the document, so the request was cancelled. No further action is needed.</td></tr>
              {% endif %}
            </table>
            """,
            Variables:
            [
                ("full_name", VariableType.String, true, null, "Nombre completo del firmante pendiente."),
                ("language", VariableType.String, true, "En", "'Es' o 'En'."),
            ]
        );

    private static NotificationTemplateSeed SignatureVerificationChallenge { get; } =
        new(
            EventKey: "sig.verification_challenge_issued.v1",
            TemplateKey: "sig.verification-challenge.v1",
            Name: "Signature — Código de verificación",
            Subject: "{% if language == 'Es' %}{{ product_name }} — Código de verificación{% else %}{{ product_name }} — Verification code{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">{% if language == 'Es' %}Verificación{% else %}Verification{% endif %}</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              {% if language == 'Es' %}
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Tu código de verificación</td></tr>
              <tr><td style="padding-bottom:4px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hola <strong style="color:#23384B;">{{ full_name }}</strong>, tu código de verificación de {{ product_name }} es:</td></tr>
              {% else %}
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Your verification code</td></tr>
              <tr><td style="padding-bottom:4px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ full_name }}</strong>, your {{ product_name }} verification code is:</td></tr>
              {% endif %}
              <tr><td style="padding:14px 0 18px 0;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF" style="background-color:#EAF4FF;border-radius:12px;"><tr><td align="center" style="padding:22px 16px;font-family:Arial,Helvetica,sans-serif;font-size:34px;line-height:40px;letter-spacing:10px;font-weight:bold;color:#1E466B;mso-line-height-rule:exactly;">{{ code }}</td></tr></table></td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">{% if language == 'Es' %}Vence el {{ expires_at }} UTC. Si no solicitaste este código, ignora este correo.{% else %}Expires on {{ expires_at }} UTC. If you did not request this code, please ignore this email.{% endif %}</td></tr>
            </table>
            """,
            Variables:
            [
                ("full_name", VariableType.String, true, null, "Nombre completo del firmante."),
                ("code", VariableType.String, true, null, "Código de verificación de un solo uso."),
                ("expires_at", VariableType.String, true, null, "Fecha de expiración ya formateada (UTC)."),
                ("language", VariableType.String, true, "En", "'Es' o 'En'."),
            ]
        );

    // PayFlow — Auth publica estos 2 eventos pre-tenant (TenantId=Guid.Empty) durante el flujo
    // pago-primero. El firstName ya viene resuelto con fallback desde el consumer.
    private static NotificationTemplateSeed OnboardingOtpRequested { get; } =
        new(
            EventKey: "onboarding.otp_requested.v1",
            TemplateKey: "onboarding.otp_code",
            Name: "Onboarding — Código de verificación",
            Subject: "{{ otp_code }} is your {{ product_name }} verification code",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Verification</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Your verification code</td></tr>
              <tr><td style="padding-bottom:4px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ first_name }}</strong>, your code to continue signing up for {{ product_name }} is:</td></tr>
              <tr><td style="padding:14px 0 18px 0;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF" style="background-color:#EAF4FF;border-radius:12px;"><tr><td align="center" style="padding:22px 16px;font-family:Arial,Helvetica,sans-serif;font-size:34px;line-height:40px;letter-spacing:10px;font-weight:bold;color:#1E466B;mso-line-height-rule:exactly;">{{ otp_code }}</td></tr></table></td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">Expires in {{ expires_in_minutes }} minutes. Never share it: the {{ product_name }} team will never ask you for it.</td></tr>
            </table>
            """,
            Variables:
            [
                ("otp_code", VariableType.String, true, null, "Código OTP de un solo uso."),
                (
                    "first_name",
                    VariableType.String,
                    true,
                    null,
                    "Nombre del comprador (con fallback ya resuelto por el consumer)."
                ),
                ("expires_in_minutes", VariableType.Number, true, null, "Minutos hasta la expiración del código."),
                (
                    "product_name",
                    VariableType.String,
                    false,
                    "TaxOffix",
                    "Branding del producto (lo inyecta el renderer desde PlatformSettings)."
                ),
            ]
        );

    // Giftcards — el PlatformAdmin envía una tarjeta por correo desde la administración (Growth publica el evento).
    private static NotificationTemplateSeed GiftcardShare { get; } =
        new(
            EventKey: "giftcards.share_requested.v1",
            TemplateKey: "giftcards.share",
            Name: "Giftcards — Tarjeta de regalo",
            Subject: "You received a {{ product_name }} gift card: {{ value_label }}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Gift card</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:14px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">You received a gift card</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ recipient_name }}</strong>, here is a gift for {{ product_name }}. It applies to any plan.</td></tr>
              {% if personal_message != '' %}<tr><td style="padding:6px 0 10px 0;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;font-style:italic;color:#496174;mso-line-height-rule:exactly;">&ldquo;{{ personal_message }}&rdquo;</td></tr>{% endif %}
              <tr><td style="padding:14px 0 6px 0;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#0B3A5B" style="background-color:#0B3A5B;border-radius:18px;"><tr><td style="padding:26px 24px;font-family:Arial,Helvetica,sans-serif;color:#FFFFFF;">
                <div style="font-size:12px;letter-spacing:1.2px;text-transform:uppercase;color:#BBD7EE;">{{ product_name }} gift card</div>
                <div style="padding:10px 0 2px 0;font-size:40px;line-height:46px;font-weight:bold;color:#FFFFFF;">{{ value_label }}</div>
                <div style="font-size:13px;color:#BBD7EE;">on any plan</div>
                <div style="padding-top:18px;"><span style="display:inline-block;background-color:#1D5280;border-radius:999px;padding:8px 16px;font-family:'Courier New',monospace;font-size:16px;font-weight:bold;letter-spacing:2px;color:#FFFFFF;">{{ code }}</span></div>
                <div style="padding-top:12px;font-size:12px;color:#BBD7EE;">{{ expires_text }}</div>
              </td></tr></table></td></tr>
              <tr><td style="padding:18px 0 6px 0;"><a href="{{ redeem_url }}" style="display:inline-block;background-color:#E3FF00;border-radius:999px;padding:12px 26px;font-family:Arial,Helvetica,sans-serif;font-size:15px;font-weight:bold;color:#0B3A5B;text-decoration:none;">Redeem your gift card</a></td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">Enter the code at sign-up, or use the button above. The discount is applied when you choose a plan.</td></tr>
            </table>
            """,
            Variables:
            [
                (
                    "recipient_name",
                    VariableType.String,
                    true,
                    null,
                    "Nombre del destinatario (con fallback ya resuelto)."
                ),
                (
                    "value_label",
                    VariableType.String,
                    true,
                    null,
                    "Beneficio tal como se ve en la tarjeta (20% OFF, $25 OFF)."
                ),
                ("code", VariableType.String, true, null, "Código de la giftcard."),
                ("expires_text", VariableType.String, true, null, "Vigencia legible: 'Valid until …' o 'No expiry'."),
                ("redeem_url", VariableType.String, true, null, "Enlace al alta con el código precargado."),
                ("personal_message", VariableType.String, false, "", "Mensaje personal opcional del remitente."),
                (
                    "product_name",
                    VariableType.String,
                    false,
                    "TaxOffix",
                    "Branding del producto (lo inyecta el renderer desde PlatformSettings)."
                ),
            ]
        );

    private static NotificationTemplateSeed OnboardingRegistrationReady { get; } =
        new(
            EventKey: "onboarding.registration_ready.v1",
            TemplateKey: "onboarding.registration_ready",
            Name: "Onboarding — Completar registro",
            Subject: "Your payment is confirmed — complete your {{ product_name }} account",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Welcome</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Complete your account</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ first_name }}</strong>, we confirmed your payment of <strong style="color:#1E466B;">{{ price_formatted }}</strong> on {{ paid_at }} for the {{ plan_name }} plan. You can now complete your account:</td></tr>
              <tr>
                <td align="left" style="padding:26px 0 18px 0;">
                  <!--[if mso]>
                  <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ registration_url }}" style="height:46px;v-text-anchor:middle;width:210px;" arcsize="22%" strokecolor="#1E466B" fillcolor="#1E466B"><w:anchorlock/><center style="color:#FFFFFF;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">Complete my account</center></v:roundrect>
                  <![endif]-->
                  <!--[if !mso]><!-- -->
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ registration_url }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Complete my account</a></td></tr></table>
                  <!--<![endif]-->
                </td>
              </tr>
              {% if receipt_download_url != blank %}
              <tr><td style="padding-bottom:16px;font-family:Arial,Helvetica,sans-serif;font-size:13px;line-height:20px;"><a href="{{ receipt_download_url }}" target="_blank" style="color:#1E466B;text-decoration:underline;">Download receipt</a></td></tr>
              {% endif %}
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">If you don't recognize this purchase, contact support immediately.</td></tr>
            </table>
            """,
            Variables:
            [
                ("first_name", VariableType.String, true, null, "Nombre del comprador."),
                (
                    "plan_name",
                    VariableType.String,
                    true,
                    null,
                    "Nombre del plan (con fallback ya resuelto por el consumer)."
                ),
                ("price_formatted", VariableType.String, true, null, "Monto ya formateado (p. ej. '49.00 USD')."),
                ("paid_at", VariableType.String, true, null, "Fecha de pago ya formateada (UTC)."),
                (
                    "registration_url",
                    VariableType.Url,
                    true,
                    null,
                    "URL de registro con el raw token, resuelta vía Auth."
                ),
                (
                    "receipt_download_url",
                    VariableType.Url,
                    false,
                    null,
                    "Link mediador de descarga del recibo, si ya llegó."
                ),
                (
                    "product_name",
                    VariableType.String,
                    false,
                    "TaxOffix",
                    "Branding del producto (lo inyecta el renderer desde PlatformSettings)."
                ),
            ]
        );

    private static NotificationTemplateSeed OnboardingReceiptReady { get; } =
        new(
            EventKey: "onboarding.receipt_ready.v1",
            TemplateKey: "onboarding.receipt_ready",
            Name: "Onboarding — Recibo disponible",
            Subject: "Your {{ product_name }} payment receipt is ready",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Receipt</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Your receipt is ready</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ first_name }}</strong>, your {{ product_name }} payment receipt is ready to download:</td></tr>
              <tr>
                <td align="left" style="padding:26px 0 4px 0;">
                  <!--[if mso]>
                  <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ receipt_download_url }}" style="height:46px;v-text-anchor:middle;width:190px;" arcsize="22%" strokecolor="#1E466B" fillcolor="#1E466B"><w:anchorlock/><center style="color:#FFFFFF;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">Download receipt</center></v:roundrect>
                  <![endif]-->
                  <!--[if !mso]><!-- -->
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ receipt_download_url }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Download receipt</a></td></tr></table>
                  <!--<![endif]-->
                </td>
              </tr>
            </table>
            """,
            Variables:
            [
                ("first_name", VariableType.String, true, null, "Nombre del comprador."),
                (
                    "receipt_download_url",
                    VariableType.Url,
                    true,
                    null,
                    "Link mediador de descarga del recibo (Auth), nunca vence."
                ),
                (
                    "product_name",
                    VariableType.String,
                    false,
                    "TaxOffix",
                    "Branding del producto (lo inyecta el renderer desde PlatformSettings)."
                ),
            ]
        );

    private static NotificationTemplateSeed OnboardingPaymentFailed { get; } =
        new(
            EventKey: "onboarding.payment_failed.v1",
            TemplateKey: "onboarding.payment_failed",
            Name: "Onboarding — Pago fallido",
            Subject: "There was a problem with your {{ product_name }} payment",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Payment</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Your payment didn't go through</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ first_name }}</strong>, we couldn't process your payment for the {{ plan_name }} plan, so your account wasn't created. No charge was made. You can try again below:</td></tr>
              {% if failure_reason != blank %}
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:13px;line-height:20px;color:#70869A;mso-line-height-rule:exactly;">Reason: {{ failure_reason }}</td></tr>
              {% endif %}
              <tr>
                <td align="left" style="padding:26px 0 18px 0;">
                  <!--[if mso]>
                  <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ retry_url }}" style="height:46px;v-text-anchor:middle;width:150px;" arcsize="22%" strokecolor="#1E466B" fillcolor="#1E466B"><w:anchorlock/><center style="color:#FFFFFF;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">Try again</center></v:roundrect>
                  <![endif]-->
                  <!--[if !mso]><!-- -->
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ retry_url }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Try again</a></td></tr></table>
                  <!--<![endif]-->
                </td>
              </tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">If you keep having trouble, reach out to support and we'll help you finish signing up.</td></tr>
            </table>
            """,
            Variables:
            [
                ("first_name", VariableType.String, true, null, "Nombre del comprador."),
                (
                    "plan_name",
                    VariableType.String,
                    true,
                    null,
                    "Nombre del plan (con fallback ya resuelto por el consumer)."
                ),
                (
                    "failure_reason",
                    VariableType.String,
                    false,
                    null,
                    "Motivo legible del fallo; si falta, se omite la línea."
                ),
                ("retry_url", VariableType.Url, true, null, "Entrada del landing con el plan/ciclo preseleccionados."),
                (
                    "product_name",
                    VariableType.String,
                    false,
                    "TaxOffix",
                    "Branding del producto (lo inyecta el renderer desde PlatformSettings)."
                ),
            ]
        );

    // Ciclo de vida de la suscripción (plan de Expiración/Dunning, Fase 3). Auth resuelve el admin y
    // publica el email-request; Notification elige uno de estos 4 por Status. Mismo palette/molde que
    // OnboardingPaymentFailed. Todos usan {{ renew_url }} (login de la oficina) como CTA.
    private static NotificationTemplateSeed SubscriptionPaymentFailed { get; } =
        new(
            EventKey: "subscription.payment_failed.v1",
            TemplateKey: "subscription.payment_failed",
            Name: "Suscripción — Pago de renovación fallido",
            Subject: "Action needed: your {{ product_name }} subscription payment failed",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Billing</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">We couldn't renew your subscription</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ first_name }}</strong>, we couldn't process the renewal for your <strong style="color:#1E466B;">{{ plan_name }}</strong> plan.{% if grace_end_date != blank %} Your access stays active until <strong style="color:#23384B;">{{ grace_end_date }}</strong>, but it will be paused if the payment isn't updated by then.{% else %} Please update your payment method to keep your access.{% endif %}</td></tr>
              {% if failure_reason != blank %}
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:13px;line-height:20px;color:#70869A;mso-line-height-rule:exactly;">Reason: {{ failure_reason }}</td></tr>
              {% endif %}
              <tr>
                <td align="left" style="padding:26px 0 18px 0;">
                  <!--[if mso]>
                  <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ renew_url }}" style="height:46px;v-text-anchor:middle;width:190px;" arcsize="22%" strokecolor="#1E466B" fillcolor="#1E466B"><w:anchorlock/><center style="color:#FFFFFF;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">Update payment</center></v:roundrect>
                  <![endif]-->
                  <!--[if !mso]><!-- -->
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ renew_url }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Update payment</a></td></tr></table>
                  <!--<![endif]-->
                </td>
              </tr>
              <tr><td style="padding-bottom:18px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF" style="background-color:#EAF4FF;border-radius:10px;"><tr><td style="padding:14px 18px;border-left:3px solid #67BAF4;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">Button not working? Copy this link into your browser:<br /><span style="word-break:break-all;color:#1E466B;">{{ renew_url }}</span></td></tr></table></td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">If you've already updated your payment method, you can ignore this email.</td></tr>
            </table>
            """,
            Variables:
            [
                ("first_name", VariableType.String, true, null, "Nombre del admin/owner del tenant."),
                ("plan_name", VariableType.String, true, "your plan", "Nombre del plan."),
                (
                    "grace_end_date",
                    VariableType.String,
                    false,
                    null,
                    "Fin de la ventana de gracia ya formateado; si falta, se omite la fecha."
                ),
                (
                    "failure_reason",
                    VariableType.String,
                    false,
                    null,
                    "Motivo legible del fallo; si falta, se omite la línea."
                ),
                ("renew_url", VariableType.Url, true, null, "Login de la oficina para actualizar el pago."),
                (
                    "product_name",
                    VariableType.String,
                    false,
                    "TaxOffix",
                    "Branding del producto (lo inyecta el renderer desde PlatformSettings)."
                ),
            ]
        );

    private static NotificationTemplateSeed SubscriptionSuspended { get; } =
        new(
            EventKey: "subscription.suspended.v1",
            TemplateKey: "subscription.suspended",
            Name: "Suscripción — Suspendida",
            Subject: "Your {{ product_name }} subscription is on hold",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Billing</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Your subscription is on hold</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ first_name }}</strong>, your <strong style="color:#1E466B;">{{ plan_name }}</strong> plan has been suspended because we couldn't collect payment. Your team's access is paused until the subscription is renewed — your data is safe in the meantime.</td></tr>
              <tr>
                <td align="left" style="padding:26px 0 18px 0;">
                  <!--[if mso]>
                  <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ renew_url }}" style="height:46px;v-text-anchor:middle;width:210px;" arcsize="22%" strokecolor="#1E466B" fillcolor="#1E466B"><w:anchorlock/><center style="color:#FFFFFF;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">Renew subscription</center></v:roundrect>
                  <![endif]-->
                  <!--[if !mso]><!-- -->
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ renew_url }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Renew subscription</a></td></tr></table>
                  <!--<![endif]-->
                </td>
              </tr>
              <tr><td style="padding-bottom:18px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF" style="background-color:#EAF4FF;border-radius:10px;"><tr><td style="padding:14px 18px;border-left:3px solid #67BAF4;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">Button not working? Copy this link into your browser:<br /><span style="word-break:break-all;color:#1E466B;">{{ renew_url }}</span></td></tr></table></td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">Need a hand? Reach out to support and we'll help you get back up and running.</td></tr>
            </table>
            """,
            Variables:
            [
                ("first_name", VariableType.String, true, null, "Nombre del admin/owner del tenant."),
                ("plan_name", VariableType.String, true, "your plan", "Nombre del plan."),
                ("renew_url", VariableType.Url, true, null, "Login de la oficina para renovar."),
                (
                    "product_name",
                    VariableType.String,
                    false,
                    "TaxOffix",
                    "Branding del producto (lo inyecta el renderer desde PlatformSettings)."
                ),
            ]
        );

    private static NotificationTemplateSeed SubscriptionAccessEnding { get; } =
        new(
            EventKey: "subscription.access_ending.v1",
            TemplateKey: "subscription.access_ending",
            Name: "Suscripción — El acceso termina pronto",
            Subject: "Your {{ product_name }} access ends on {{ access_ends_date }}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:11px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;">Billing</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" cellpadding="0" cellspacing="0" border="0" width="40"><tr><td height="3" bgcolor="#67BAF4" style="font-size:0;line-height:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:26px;font-weight:bold;color:#23384B;padding-bottom:14px;">Your access ends soon</td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:23px;color:#496174;">Hi <strong style="color:#23384B;">{{ first_name }}</strong>, you cancelled your <strong style="color:#1E466B;">{{ plan_name }}</strong> plan and it ends on <strong style="color:#1E466B;">{{ access_ends_date }}</strong>. That's in {{ days_until_end }} day(s). If you'd like to stay, you can undo the cancellation before then and nothing will have changed.</td></tr>
              <tr>
                <td align="left" style="padding:26px 0 18px 0;">
                  <!--[if mso]>
                  <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ renew_url }}" style="height:44px;v-text-anchor:middle;width:230px;" arcsize="23%" stroke="f" fillcolor="#1E466B"><w:anchorlock/><center style="color:#ffffff;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">Keep my subscription</center></v:roundrect>
                  <![endif]-->
                  <!--[if !mso]><!-- -->
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="border-radius:10px;"><a href="{{ renew_url }}" target="_blank" style="display:inline-block;padding:13px 28px;font-family:Arial,Helvetica,sans-serif;font-size:15px;font-weight:bold;color:#ffffff;text-decoration:none;">Keep my subscription</a></td></tr></table>
                  <!--<![endif]-->
                </td>
              </tr>
              <tr><td style="padding-bottom:18px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF"><tr><td style="padding:12px 14px;border-left:3px solid #67BAF4;font-family:Arial,Helvetica,sans-serif;font-size:13px;color:#496174;">Button not working? Copy this link into your browser:<br /><span style="word-break:break-all;color:#1E466B;">{{ renew_url }}</span></td></tr></table></td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:13px;line-height:20px;color:#70869A;">If you'd rather go ahead, no action is needed. Your data stays safe and you can come back whenever you want.</td></tr>
            </table>
            """,
            Variables:
            [
                ("first_name", VariableType.String, true, null, "Nombre del admin/owner del tenant."),
                ("plan_name", VariableType.String, true, "your plan", "Nombre del plan."),
                ("access_ends_date", VariableType.String, true, null, "Fecha en la que termina el acceso."),
                ("days_until_end", VariableType.String, true, "0", "Días que faltan para esa fecha."),
                ("renew_url", VariableType.Url, true, null, "Login de la oficina para deshacer la cancelación."),
                (
                    "product_name",
                    VariableType.String,
                    false,
                    "TaxOffix",
                    "Branding del producto (lo inyecta el renderer desde PlatformSettings)."
                ),
            ]
        );

    private static NotificationTemplateSeed SubscriptionCancellationScheduled { get; } =
        new(
            EventKey: "subscription.cancellation_scheduled.v1",
            TemplateKey: "subscription.cancellation_scheduled",
            Name: "Suscripción — Cancelación programada",
            Subject: "Your {{ product_name }} subscription ends on {{ access_ends_date }}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:11px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;">Billing</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" cellpadding="0" cellspacing="0" border="0" width="40"><tr><td height="3" bgcolor="#67BAF4" style="font-size:0;line-height:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:26px;font-weight:bold;color:#23384B;padding-bottom:14px;">Your subscription will end</td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:23px;color:#496174;">Hi <strong style="color:#23384B;">{{ first_name }}</strong>, we've scheduled the cancellation of your <strong style="color:#1E466B;">{{ plan_name }}</strong> plan. Nothing changes today: you keep full access until <strong style="color:#1E466B;">{{ access_ends_date }}</strong>, the end of the period you already paid for. We won't charge you again.</td></tr>
              <tr>
                <td align="left" style="padding:26px 0 18px 0;">
                  <!--[if mso]>
                  <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ renew_url }}" style="height:44px;v-text-anchor:middle;width:230px;" arcsize="23%" stroke="f" fillcolor="#1E466B"><w:anchorlock/><center style="color:#ffffff;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">Keep my subscription</center></v:roundrect>
                  <![endif]-->
                  <!--[if !mso]><!-- -->
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="border-radius:10px;"><a href="{{ renew_url }}" target="_blank" style="display:inline-block;padding:13px 28px;font-family:Arial,Helvetica,sans-serif;font-size:15px;font-weight:bold;color:#ffffff;text-decoration:none;">Keep my subscription</a></td></tr></table>
                  <!--<![endif]-->
                </td>
              </tr>
              <tr><td style="padding-bottom:18px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF"><tr><td style="padding:12px 14px;border-left:3px solid #67BAF4;font-family:Arial,Helvetica,sans-serif;font-size:13px;color:#496174;">Button not working? Copy this link into your browser:<br /><span style="word-break:break-all;color:#1E466B;">{{ renew_url }}</span></td></tr></table></td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:13px;line-height:20px;color:#70869A;">Changed your mind? You can undo this any time before {{ access_ends_date }} and nothing will have happened. After that date your data stays safe — you can come back whenever you want.</td></tr>
            </table>
            """,
            Variables:
            [
                ("first_name", VariableType.String, true, null, "Nombre del admin/owner del tenant."),
                ("plan_name", VariableType.String, true, "your plan", "Nombre del plan."),
                ("access_ends_date", VariableType.String, true, null, "Fecha hasta la que llega el acceso pagado."),
                ("renew_url", VariableType.Url, true, null, "Login de la oficina para deshacer la cancelación."),
                (
                    "product_name",
                    VariableType.String,
                    false,
                    "TaxOffix",
                    "Branding del producto (lo inyecta el renderer desde PlatformSettings)."
                ),
            ]
        );

    // D11 del plan único: una migración agenda otra edición del plan para la oficina.
    private static NotificationTemplateSeed SubscriptionVersionChangeScheduled { get; } =
        new(
            EventKey: "subscription.version_change_scheduled.v1",
            TemplateKey: "subscription.version_change_scheduled",
            Name: "Suscripción — Cambio de condiciones del plan",
            Subject: "Your {{ product_name }} plan terms change on {{ change_date }}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:11px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;">Billing</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" cellpadding="0" cellspacing="0" border="0" width="40"><tr><td height="3" bgcolor="#67BAF4" style="font-size:0;line-height:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:26px;font-weight:bold;color:#23384B;padding-bottom:14px;">Your plan terms will change</td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:23px;color:#496174;">Hi <strong style="color:#23384B;">{{ first_name }}</strong>, we're updating the terms of your <strong style="color:#1E466B;">{{ plan_name }}</strong> plan. Nothing changes today: the new terms start with your renewal on <strong style="color:#1E466B;">{{ change_date }}</strong>.</td></tr>
              <tr><td style="padding:18px 0;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF"><tr><td style="padding:12px 14px;border-left:3px solid #67BAF4;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#23384B;"><strong>What changes:</strong> {{ change_summary }}</td></tr></table></td></tr>
              <tr>
                <td align="left" style="padding:8px 0 18px 0;">
                  <!--[if mso]>
                  <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ manage_url }}" style="height:44px;v-text-anchor:middle;width:230px;" arcsize="23%" stroke="f" fillcolor="#1E466B"><w:anchorlock/><center style="color:#ffffff;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">Review my subscription</center></v:roundrect>
                  <![endif]-->
                  <!--[if !mso]><!-- -->
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="border-radius:10px;"><a href="{{ manage_url }}" target="_blank" style="display:inline-block;padding:13px 28px;font-family:Arial,Helvetica,sans-serif;font-size:15px;font-weight:bold;color:#ffffff;text-decoration:none;">Review my subscription</a></td></tr></table>
                  <!--<![endif]-->
                </td>
              </tr>
              <tr><td style="padding-bottom:18px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF"><tr><td style="padding:12px 14px;border-left:3px solid #67BAF4;font-family:Arial,Helvetica,sans-serif;font-size:13px;color:#496174;">Button not working? Copy this link into your browser:<br /><span style="word-break:break-all;color:#1E466B;">{{ manage_url }}</span></td></tr></table></td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:13px;line-height:20px;color:#70869A;">Until {{ change_date }} you keep your current terms. If you'd rather change plans or cancel before then, you can do it from Manage subscription.</td></tr>
            </table>
            """,
            Variables:
            [
                ("first_name", VariableType.String, true, null, "Nombre del admin/owner del tenant."),
                ("plan_name", VariableType.String, true, "your plan", "Nombre del plan."),
                ("change_date", VariableType.String, true, null, "Fecha en que entran las condiciones nuevas."),
                (
                    "change_summary",
                    VariableType.String,
                    true,
                    null,
                    "Qué cambia (precio, usuarios, almacenamiento, módulos)."
                ),
                ("manage_url", VariableType.Url, true, null, "Manage subscription de la oficina."),
                (
                    "product_name",
                    VariableType.String,
                    false,
                    "TaxOffix",
                    "Branding del producto (lo inyecta el renderer desde PlatformSettings)."
                ),
            ]
        );

    private static NotificationTemplateSeed SubscriptionBillingCycleChanged { get; } =
        new(
            EventKey: "subscription.billing_cycle_changed.v1",
            TemplateKey: "subscription.billing_cycle_changed",
            Name: "Suscripción — Cambio de ciclo de facturación",
            Subject: "Your {{ product_name }} subscription is now billed every {{ cadence }}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:11px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;">Billing</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" cellpadding="0" cellspacing="0" border="0" width="40"><tr><td height="3" bgcolor="#67BAF4" style="font-size:0;line-height:0;">&nbsp;</td></tr></table></td></tr>
              {% if date_charged != "" %}
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:26px;font-weight:bold;color:#23384B;padding-bottom:14px;">You're now on {{ cadence }}ly billing</td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:23px;color:#496174;">Hi <strong style="color:#23384B;">{{ first_name }}</strong>, your <strong style="color:#1E466B;">{{ plan_name }}</strong> plan, add-ons and extra seats are now billed every {{ cadence }}. We charged <strong style="color:#1E466B;">${{ amount }}</strong> on {{ date_charged }}; your receipt lists each item.</td></tr>
              <tr><td style="padding:18px 0;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF"><tr><td style="padding:12px 14px;border-left:3px solid #67BAF4;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#23384B;"><strong>Next renewal:</strong> {{ next_charge_date }}</td></tr></table></td></tr>
              {% else %}
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:26px;font-weight:bold;color:#23384B;padding-bottom:14px;">Your billing will change to every {{ cadence }}</td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:23px;color:#496174;">Hi <strong style="color:#23384B;">{{ first_name }}</strong>, nothing changes today. Starting <strong style="color:#1E466B;">{{ next_charge_date }}</strong>, your <strong style="color:#1E466B;">{{ plan_name }}</strong> plan, add-ons and extra seats will be billed every {{ cadence }} at the current prices.</td></tr>
              {% endif %}
              <tr>
                <td align="left" style="padding:8px 0 18px 0;">
                  <!--[if mso]>
                  <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ manage_url }}" style="height:44px;v-text-anchor:middle;width:230px;" arcsize="23%" stroke="f" fillcolor="#1E466B"><w:anchorlock/><center style="color:#ffffff;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">Review my subscription</center></v:roundrect>
                  <![endif]-->
                  <!--[if !mso]><!-- -->
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="border-radius:10px;"><a href="{{ manage_url }}" target="_blank" style="display:inline-block;padding:13px 28px;font-family:Arial,Helvetica,sans-serif;font-size:15px;font-weight:bold;color:#ffffff;text-decoration:none;">Review my subscription</a></td></tr></table>
                  <!--<![endif]-->
                </td>
              </tr>
              <tr><td style="padding-bottom:18px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF"><tr><td style="padding:12px 14px;border-left:3px solid #67BAF4;font-family:Arial,Helvetica,sans-serif;font-size:13px;color:#496174;">Button not working? Copy this link into your browser:<br /><span style="word-break:break-all;color:#1E466B;">{{ manage_url }}</span></td></tr></table></td></tr>
            </table>
            """,
            Variables:
            [
                ("first_name", VariableType.String, true, null, "Nombre del admin/owner del tenant."),
                ("plan_name", VariableType.String, true, "your plan", "Nombre del plan."),
                ("cadence", VariableType.String, true, "month", "Ciclo nuevo: month o year."),
                (
                    "amount",
                    VariableType.String,
                    false,
                    "",
                    "Total cobrado al pasar a anual (vacío al pasar a mensual)."
                ),
                (
                    "date_charged",
                    VariableType.String,
                    false,
                    "",
                    "Fecha del cobro (vacía si el cambio solo se agendó)."
                ),
                (
                    "next_charge_date",
                    VariableType.String,
                    true,
                    null,
                    "Próxima renovación, o desde cuándo rige el ciclo nuevo."
                ),
                ("manage_url", VariableType.Url, true, null, "Manage subscription de la oficina."),
                (
                    "product_name",
                    VariableType.String,
                    false,
                    "TaxOffix",
                    "Branding del producto (lo inyecta el renderer desde PlatformSettings)."
                ),
            ],
            ContentVersion: 28
        );

    private static NotificationTemplateSeed SubscriptionExpired { get; } =
        new(
            EventKey: "subscription.expired.v1",
            TemplateKey: "subscription.expired",
            Name: "Suscripción — Expirada",
            Subject: "Your {{ product_name }} subscription has expired",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Billing</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Your subscription has expired</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ first_name }}</strong>, your <strong style="color:#1E466B;">{{ plan_name }}</strong> plan has expired and access is now closed. Renew whenever you're ready to pick up right where you left off — your data is safe and waiting.</td></tr>
              <tr>
                <td align="left" style="padding:26px 0 18px 0;">
                  <!--[if mso]>
                  <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ renew_url }}" style="height:46px;v-text-anchor:middle;width:210px;" arcsize="22%" strokecolor="#1E466B" fillcolor="#1E466B"><w:anchorlock/><center style="color:#FFFFFF;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">Renew subscription</center></v:roundrect>
                  <![endif]-->
                  <!--[if !mso]><!-- -->
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ renew_url }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Renew subscription</a></td></tr></table>
                  <!--<![endif]-->
                </td>
              </tr>
              <tr><td style="padding-bottom:18px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF" style="background-color:#EAF4FF;border-radius:10px;"><tr><td style="padding:14px 18px;border-left:3px solid #67BAF4;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">Button not working? Copy this link into your browser:<br /><span style="word-break:break-all;color:#1E466B;">{{ renew_url }}</span></td></tr></table></td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">If you'd rather not renew, no action is needed. Your account will stay closed until you come back.</td></tr>
            </table>
            """,
            Variables:
            [
                ("first_name", VariableType.String, true, null, "Nombre del admin/owner del tenant."),
                ("plan_name", VariableType.String, true, "your plan", "Nombre del plan."),
                ("renew_url", VariableType.Url, true, null, "Login de la oficina para renovar."),
                (
                    "product_name",
                    VariableType.String,
                    false,
                    "TaxOffix",
                    "Branding del producto (lo inyecta el renderer desde PlatformSettings)."
                ),
            ]
        );

    private static NotificationTemplateSeed SubscriptionRenewalUpcoming { get; } =
        new(
            EventKey: "subscription.renewal_upcoming.v1",
            TemplateKey: "subscription.renewal_upcoming",
            Name: "Suscripción — Renovación próxima",
            Subject: "Your {{ product_name }} subscription renews in {{ days_until_renewal }} day(s)",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:11px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;">Billing</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" cellpadding="0" cellspacing="0" border="0" width="40"><tr><td height="3" bgcolor="#67BAF4" style="font-size:0;line-height:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:26px;font-weight:bold;color:#23384B;padding-bottom:14px;">Your subscription renews soon</td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:23px;color:#496174;">Hi <strong style="color:#23384B;">{{ first_name }}</strong>, just a heads up — your <strong style="color:#1E466B;">{{ plan_name }}</strong> plan renews on <strong style="color:#1E466B;">{{ renewal_date }}</strong> ({{ days_until_renewal }} day(s) from now). Nothing to do: we'll charge your card on file automatically.</td></tr>
              {% if amount != "" %}
              <tr><td style="padding:18px 0 0 0;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF"><tr><td style="padding:12px 14px;border-left:3px solid #67BAF4;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#23384B;"><strong>Amount:</strong> ${{ amount }} every {{ cadence }}, including your add-ons and extra seats.</td></tr></table></td></tr>
              {% endif %}
              <tr>
                <td align="left" style="padding:26px 0 18px 0;">
                  <!--[if mso]>
                  <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ renew_url }}" style="height:44px;v-text-anchor:middle;width:230px;" arcsize="23%" stroke="f" fillcolor="#1E466B"><w:anchorlock/><center style="color:#ffffff;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">Manage subscription</center></v:roundrect>
                  <![endif]-->
                  <!--[if !mso]><!-- -->
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="border-radius:10px;"><a href="{{ renew_url }}" target="_blank" style="display:inline-block;padding:13px 28px;font-family:Arial,Helvetica,sans-serif;font-size:15px;font-weight:bold;color:#ffffff;text-decoration:none;">Manage subscription</a></td></tr></table>
                  <!--<![endif]-->
                </td>
              </tr>
              <tr><td style="padding-bottom:18px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF"><tr><td style="padding:12px 14px;border-left:3px solid #67BAF4;font-family:Arial,Helvetica,sans-serif;font-size:13px;color:#496174;">Button not working? Copy this link into your browser:<br /><span style="word-break:break-all;color:#1E466B;">{{ renew_url }}</span></td></tr></table></td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:13px;line-height:20px;color:#70869A;">Want to make a change? You can switch plans, update your card or cancel from the dashboard before the renewal date.</td></tr>
            </table>
            """,
            Variables:
            [
                ("first_name", VariableType.String, true, null, "Nombre del admin/owner del tenant."),
                ("plan_name", VariableType.String, true, "your plan", "Nombre del plan."),
                ("days_until_renewal", VariableType.String, true, null, "Días que faltan para la renovación."),
                ("renewal_date", VariableType.String, true, null, "Fecha de la próxima renovación."),
                ("amount", VariableType.String, false, "", "Total de la renovación: plan más add-ons y asientos (E4)."),
                ("cadence", VariableType.String, false, "month", "Ciclo: month o year."),
                ("renew_url", VariableType.Url, true, null, "Login de la oficina para gestionar la suscripción."),
                (
                    "product_name",
                    VariableType.String,
                    false,
                    "TaxOffix",
                    "Branding del producto (lo inyecta el renderer desde PlatformSettings)."
                ),
            ],
            ContentVersion: 29
        );

    private static NotificationTemplateSeed SubscriptionReactivated { get; } =
        new(
            EventKey: "subscription.reactivated.v1",
            TemplateKey: "subscription.reactivated",
            Name: "Suscripción — Reactivada",
            Subject: "Your {{ product_name }} subscription is active again",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Billing</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">You're all set — welcome back</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ first_name }}</strong>, good news: your <strong style="color:#1E466B;">{{ plan_name }}</strong> plan is active again and full access has been restored for your whole team.</td></tr>
              <tr>
                <td align="left" style="padding:26px 0 18px 0;">
                  <!--[if mso]>
                  <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ renew_url }}" style="height:46px;v-text-anchor:middle;width:210px;" arcsize="22%" strokecolor="#1E466B" fillcolor="#1E466B"><w:anchorlock/><center style="color:#FFFFFF;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">Go to your workspace</center></v:roundrect>
                  <![endif]-->
                  <!--[if !mso]><!-- -->
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ renew_url }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Go to your workspace</a></td></tr></table>
                  <!--<![endif]-->
                </td>
              </tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">Thanks for staying with {{ product_name }}. If anything looks off, reach out to support anytime.</td></tr>
            </table>
            """,
            Variables:
            [
                ("first_name", VariableType.String, true, null, "Nombre del admin/owner del tenant."),
                ("plan_name", VariableType.String, true, "your plan", "Nombre del plan."),
                ("renew_url", VariableType.Url, true, null, "Login de la oficina."),
                (
                    "product_name",
                    VariableType.String,
                    false,
                    "TaxOffix",
                    "Branding del producto (lo inyecta el renderer desde PlatformSettings)."
                ),
            ]
        );

    /// <summary>Confirmación tras un cobro recurrente off-session exitoso (Automatic).</summary>
    private static NotificationTemplateSeed SubscriptionRecurringCharge { get; } =
        new(
            EventKey: "subscription.recurring_charge.v1",
            TemplateKey: "subscription.recurring_charge",
            Name: "Suscripción — Cobro recurrente exitoso",
            Subject: "Payment received — your {{ product_name }} subscription continues",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Billing</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Payment received</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ first_name }}</strong>, we charged <strong style="color:#1E466B;">${{ amount }}</strong> to your {{ card_brand }} ending {{ card_last4 }} for your <strong style="color:#1E466B;">{{ plan_name }}</strong> plan.{% if next_charge_date != blank %} The next charge is scheduled for <strong style="color:#23384B;">{{ next_charge_date }}</strong>.{% endif %}</td></tr>
              <tr>
                <td align="left" style="padding:22px 0 4px 0;">
                  {% if receipt_url != blank %}
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0" style="display:inline-block;margin-right:10px;"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ receipt_url }}" target="_blank" style="display:inline-block;padding:12px 24px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">View receipt</a></td></tr></table>
                  {% endif %}
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0" style="display:inline-block;"><tr><td align="center" style="border:1px solid #1E466B;border-radius:10px;"><a href="{{ manage_url }}" target="_blank" style="display:inline-block;padding:11px 22px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:18px;font-weight:bold;color:#1E466B;text-decoration:none;">Manage billing</a></td></tr></table>
                </td>
              </tr>
              <tr><td style="padding-top:18px;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">You turned on automatic renewal on this subscription. You can switch back to manual any time from Manage billing.</td></tr>
            </table>
            """,
            Variables:
            [
                ("first_name", VariableType.String, true, null, "Nombre del admin/owner del tenant."),
                ("plan_name", VariableType.String, true, "your plan", "Nombre del plan."),
                ("amount", VariableType.String, true, null, "Importe cobrado ya formateado."),
                ("date_charged", VariableType.String, false, null, "Fecha del cobro ya formateada."),
                ("card_brand", VariableType.String, true, "card", "Marca del método guardado."),
                ("card_last4", VariableType.String, true, "----", "Últimos 4 del método guardado."),
                ("receipt_url", VariableType.Url, false, null, "URL del recibo del proveedor (opcional)."),
                ("next_charge_date", VariableType.String, false, null, "Fecha del próximo cobro ya formateada."),
                ("manage_url", VariableType.Url, true, null, "URL a Manage Subscription."),
                (
                    "product_name",
                    VariableType.String,
                    false,
                    "TaxOffix",
                    "Branding del producto (lo inyecta el renderer desde PlatformSettings)."
                ),
            ]
        );

    /// <summary>
    /// Cobro automático de renovación fallido (plan, asiento o add-on), uno por intento. Dos variantes:
    /// quedan reintentos (fecha del próximo) o fue el último (qué se suspendió). Lleva a Manage subscription.
    /// v18: reescrito — antes solo salía en el primer cobro del plan, con la marca "Stripe" y "$" fijo.
    /// </summary>
    private static NotificationTemplateSeed SubscriptionRecurringChargeFailed { get; } =
        new(
            EventKey: "subscription.recurring_charge_failed.v1",
            TemplateKey: "subscription.recurring_charge_failed",
            Name: "Suscripción — Cobro automático fallido",
            Subject: "{% if is_final %}Final notice: we couldn't process your {{ product_name }} payment{% else %}Action needed: we couldn't process your {{ product_name }} payment{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Billing</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="{% if is_final %}#D65B5B{% else %}#67BAF4{% endif %}" style="background-color:{% if is_final %}#D65B5B{% else %}#67BAF4{% endif %};height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">{% if is_final %}Your payment couldn't be completed{% else %}We couldn't process your payment{% endif %}</td></tr>
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ first_name }}</strong>, {% if is_final %}we made our final attempt to charge{% else %}we attempted to charge{% endif %} {% if has_card %}your <strong style="color:#1E466B;">{{ card_brand }} ending in {{ card_last4 }}</strong>{% else %}your account{% endif %} for <strong style="color:#1E466B;">{{ item_description }}</strong>, but the payment was not completed.</td></tr>
              {% if is_final %}
              <tr><td style="padding-bottom:18px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#FDEEEE" style="background-color:#FDEEEE;border-radius:10px;"><tr><td style="padding:14px 18px;border-left:3px solid #D65B5B;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#23384B;mso-line-height-rule:exactly;">As a result, <strong>{{ suspension_effect }}</strong>.</td></tr></table></td></tr>
              {% endif %}
              <tr>
                <td style="padding-bottom:20px;">
                  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="border:1px solid #E3E9EF;border-radius:10px;">
                    <tr>
                      <td style="padding:12px 18px;border-bottom:1px solid #E3E9EF;font-family:Arial,Helvetica,sans-serif;font-size:13px;line-height:20px;color:#70869A;mso-line-height-rule:exactly;">Amount</td>
                      <td align="right" style="padding:12px 18px;border-bottom:1px solid #E3E9EF;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;font-weight:bold;color:#23384B;mso-line-height-rule:exactly;">{{ amount }}</td>
                    </tr>
                    <tr>
                      <td style="padding:12px 18px;border-bottom:1px solid #E3E9EF;font-family:Arial,Helvetica,sans-serif;font-size:13px;line-height:20px;color:#70869A;mso-line-height-rule:exactly;">Item</td>
                      <td align="right" style="padding:12px 18px;border-bottom:1px solid #E3E9EF;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#23384B;mso-line-height-rule:exactly;">{{ item_description }}</td>
                    </tr>
                    {% if attempt_number != blank and max_attempts != blank %}
                    <tr>
                      <td style="padding:12px 18px;border-bottom:1px solid #E3E9EF;font-family:Arial,Helvetica,sans-serif;font-size:13px;line-height:20px;color:#70869A;mso-line-height-rule:exactly;">Attempt</td>
                      <td align="right" style="padding:12px 18px;border-bottom:1px solid #E3E9EF;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#23384B;mso-line-height-rule:exactly;">{{ attempt_number }} of {{ max_attempts }}</td>
                    </tr>
                    {% endif %}
                    <tr>
                      <td style="padding:12px 18px;{% unless is_final %}border-bottom:1px solid #E3E9EF;{% endunless %}font-family:Arial,Helvetica,sans-serif;font-size:13px;line-height:20px;color:#70869A;mso-line-height-rule:exactly;">Reason</td>
                      <td align="right" style="padding:12px 18px;{% unless is_final %}border-bottom:1px solid #E3E9EF;{% endunless %}font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#23384B;mso-line-height-rule:exactly;">{{ friendly_reason }}</td>
                    </tr>
                    {% unless is_final %}
                    <tr>
                      <td style="padding:12px 18px;font-family:Arial,Helvetica,sans-serif;font-size:13px;line-height:20px;color:#70869A;mso-line-height-rule:exactly;">Next attempt</td>
                      <td align="right" style="padding:12px 18px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;font-weight:bold;color:#1E466B;mso-line-height-rule:exactly;">{{ next_retry_date }}</td>
                    </tr>
                    {% endunless %}
                  </table>
                </td>
              </tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">{% if is_final %}Your data is safe and nothing has been deleted. To restore access right away, complete the payment in Manage subscription.{% else %}<strong style="color:#23384B;">What happens next:</strong> we'll automatically try again on {{ next_retry_date }}. To keep your team's access uninterrupted, please review your payment method or complete the payment now.{% endif %}</td></tr>
              <tr>
                <td align="left" style="padding:22px 0 4px 0;">
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ manage_url }}" target="_blank" style="display:inline-block;padding:13px 26px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Go to Manage subscription</a></td></tr></table>
                </td>
              </tr>
              <tr><td style="padding-top:18px;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">{% unless is_final %}If you've already taken care of this, no further action is needed. {% endunless %}Need a hand? Our support team is happy to help you get back on track.</td></tr>
            </table>
            """,
            Variables:
            [
                ("first_name", VariableType.String, true, null, "Nombre del admin principal del tenant."),
                (
                    "item_description",
                    VariableType.String,
                    true,
                    "your subscription",
                    "Qué se intentó cobrar, en inglés (\"Professional plan\", \"1 additional user seat\")."
                ),
                ("amount", VariableType.String, true, null, "Importe con moneda ya formateado (\"$49.00\")."),
                ("has_card", VariableType.Bool, true, "false", "true si se conoce la tarjeta guardada."),
                ("card_brand", VariableType.String, false, "card", "Marca de la tarjeta guardada (Visa, Mastercard…)."),
                ("card_last4", VariableType.String, false, null, "Últimos 4 de la tarjeta guardada."),
                ("attempt_number", VariableType.String, false, null, "Número de este intento (1 = primer cobro)."),
                ("max_attempts", VariableType.String, false, null, "Intentos del ciclo de cobro."),
                ("is_final", VariableType.Bool, true, "false", "true si fue el último intento (sin reintentos)."),
                (
                    "next_retry_date",
                    VariableType.String,
                    false,
                    null,
                    "Fecha del próximo intento; vacía si es el último."
                ),
                (
                    "friendly_reason",
                    VariableType.String,
                    true,
                    "The payment could not be completed.",
                    "Motivo en palabras de cliente (nunca el código crudo del proveedor)."
                ),
                (
                    "suspension_effect",
                    VariableType.String,
                    false,
                    null,
                    "Qué se suspendió tras el último intento, en minúscula para seguir la frase \"As a result, …\"."
                ),
                ("manage_url", VariableType.Url, true, null, "Account del Landing, sección Plan (Pay now)."),
                (
                    "product_name",
                    VariableType.String,
                    false,
                    "TaxOffix",
                    "Branding del producto (lo inyecta el renderer desde PlatformSettings)."
                ),
            ],
            ContentVersion: 18
        );

    /// <summary>Confirmación del cambio Auto↔Manual.</summary>
    private static NotificationTemplateSeed SubscriptionBillingModeChanged { get; } =
        new(
            EventKey: "subscription.billing_mode_changed.v1",
            TemplateKey: "subscription.billing_mode_changed",
            Name: "Suscripción — Cambio de preferencia de facturación",
            Subject: "Your billing preference changed to {{ new_mode }}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Billing</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Billing preference updated</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ first_name }}</strong>, your billing preference on {{ product_name }} is now <strong style="color:#1E466B;">{{ new_mode }}</strong>.{% if new_mode == 'Automatic' %} We'll renew your subscription automatically at the end of each period.{% else %} We'll remind you to pay at the end of each period — nothing is charged until you do.{% endif %}</td></tr>
              <tr>
                <td align="left" style="padding:22px 0 4px 0;">
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ manage_url }}" target="_blank" style="display:inline-block;padding:12px 24px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Manage billing</a></td></tr></table>
                </td>
              </tr>
              <tr><td style="padding-top:18px;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">Changed by mistake? Open Manage billing and switch back any time.</td></tr>
            </table>
            """,
            Variables:
            [
                ("first_name", VariableType.String, true, null, "Nombre del admin/owner del tenant."),
                ("new_mode", VariableType.String, true, null, "Automatic | Manual."),
                ("manage_url", VariableType.Url, true, null, "URL a Manage Subscription."),
                (
                    "product_name",
                    VariableType.String,
                    false,
                    "TaxOffix",
                    "Branding del producto (lo inyecta el renderer desde PlatformSettings)."
                ),
            ]
        );

    /// <summary>Alta de mandate la primera vez que el user eligió Automatic.</summary>
    private static NotificationTemplateSeed SubscriptionMandateCreated { get; } =
        new(
            EventKey: "subscription.mandate_created.v1",
            TemplateKey: "subscription.mandate_created",
            Name: "Suscripción — Automático habilitado",
            Subject: "Automatic renewal enabled on your {{ product_name }} subscription",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Billing</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Automatic renewal enabled</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ first_name }}</strong>, we'll automatically charge your {{ card_brand }} ending {{ card_last4 }} <strong style="color:#1E466B;">${{ amount }}</strong> every {{ cadence }} for your <strong style="color:#1E466B;">{{ plan_name }}</strong> plan.</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">You'll get a receipt every time we charge, and you can switch to manual any time.</td></tr>
              <tr>
                <td align="left" style="padding:22px 0 4px 0;">
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ manage_url }}" target="_blank" style="display:inline-block;padding:12px 24px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Manage billing</a></td></tr></table>
                </td>
              </tr>
            </table>
            """,
            Variables:
            [
                ("first_name", VariableType.String, true, null, "Nombre del admin/owner del tenant."),
                ("plan_name", VariableType.String, true, "your plan", "Nombre del plan."),
                ("amount", VariableType.String, true, null, "Importe acordado ya formateado."),
                ("cadence", VariableType.String, true, "month", "Cadencia (month/year)."),
                ("card_brand", VariableType.String, true, "card", "Marca del método guardado."),
                ("card_last4", VariableType.String, true, "----", "Últimos 4 del método guardado."),
                ("manage_url", VariableType.Url, true, null, "URL a Manage Subscription."),
                (
                    "product_name",
                    VariableType.String,
                    false,
                    "TaxOffix",
                    "Branding del producto (lo inyecta el renderer desde PlatformSettings)."
                ),
            ]
        );

    /// <summary>
    /// Reminder Fase 10 — el template que la Fase 8 difirió a propósito hasta que Notification tuvo el
    /// directorio <c>userId → email</c>. El H1 usa el {{ title }} del recordatorio.
    /// </summary>
    private static NotificationTemplateSeed ReminderDue { get; } =
        new(
            EventKey: "reminder.due.v1",
            TemplateKey: "reminder.due",
            Name: "Reminder — Recordatorio vencido",
            Subject: "{{ title }}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Reminder</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:14px;font-family:Arial,Helvetica,sans-serif;font-size:24px;line-height:32px;font-weight:bold;letter-spacing:-0.3px;color:#23384B;mso-line-height-rule:exactly;">{{ title }}</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">{{ body }}</td></tr>
              {% if snooze_count > 0 %}
              <tr><td style="padding-top:4px;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:18px;color:#70869A;mso-line-height-rule:exactly;">Snoozed {{ snooze_count }} time(s).</td></tr>
              {% endif %}
              <tr>
                <td align="left" style="padding:26px 0 4px 0;">
                  <!--[if mso]>
                  <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ portal_link }}" style="height:46px;v-text-anchor:middle;width:200px;" arcsize="22%" strokecolor="#1E466B" fillcolor="#1E466B"><w:anchorlock/><center style="color:#FFFFFF;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">Open {{ product_name }}</center></v:roundrect>
                  <![endif]-->
                  <!--[if !mso]><!-- -->
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ portal_link }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Open {{ product_name }}</a></td></tr></table>
                  <!--<![endif]-->
                </td>
              </tr>
            </table>
            """,
            Variables:
            [
                (
                    "title",
                    VariableType.String,
                    true,
                    null,
                    "Título del recordatorio (con el sufijo de pospuesto si aplica)."
                ),
                (
                    "body",
                    VariableType.String,
                    true,
                    null,
                    "Cuerpo del usuario, o la hora del ancla en su zona horaria."
                ),
                ("category", VariableType.String, true, "General", "General | Calendar | Task | Note."),
                ("snooze_count", VariableType.Number, true, "0", "Cuántas veces se pospuso."),
                ("portal_link", VariableType.Url, true, null, "URL base del portal."),
                (
                    "product_name",
                    VariableType.String,
                    false,
                    "TaxOffix",
                    "Branding del producto (lo inyecta el renderer desde PlatformSettings)."
                ),
            ]
        );

    private static NotificationTemplateSeed TaskWaitingOnClient { get; } =
        new(
            EventKey: "task.waiting_on_client.v1",
            TemplateKey: "task.waiting_on_client.v1",
            Name: "Task — Documentación pendiente del cliente",
            Subject: "{{ product_name }} — We're missing documents from you",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Documents</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:16px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">We're missing some documents</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ customer_name }}</strong>, to continue with <strong style="color:#1E466B;">{{ task_title }}</strong>{% if tax_year %} (tax year {{ tax_year }}){% endif %} we need you to send us:</td></tr>
              <tr><td style="padding-bottom:12px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF" style="background-color:#EAF4FF;border-radius:10px;"><tr><td style="padding:14px 18px;border-left:3px solid #67BAF4;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#23384B;mso-line-height-rule:exactly;">{{ expected_items }}</td></tr></table></td></tr>
              {% if client_due_at_utc %}
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#496174;mso-line-height-rule:exactly;">Please send it before <strong style="color:#23384B;">{{ client_due_at_utc }}</strong>.</td></tr>
              {% endif %}
              <tr>
                <td align="left" style="padding:24px 0 4px 0;">
                  <!--[if mso]>
                  <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ portal_link }}" style="height:46px;v-text-anchor:middle;width:200px;" arcsize="22%" strokecolor="#1E466B" fillcolor="#1E466B"><w:anchorlock/><center style="color:#FFFFFF;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">Upload documents</center></v:roundrect>
                  <![endif]-->
                  <!--[if !mso]><!-- -->
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ portal_link }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Upload documents</a></td></tr></table>
                  <!--<![endif]-->
                </td>
              </tr>
            </table>
            """,
            Variables:
            [
                ("customer_name", VariableType.String, true, null, "Nombre del cliente, del directorio local."),
                ("task_title", VariableType.String, true, null, "Título de la tarea que quedó esperando."),
                (
                    "expected_items",
                    VariableType.String,
                    true,
                    null,
                    "Qué se le pide al cliente, tal como lo escribió el preparador."
                ),
                (
                    "client_due_at_utc",
                    VariableType.String,
                    false,
                    null,
                    "Para cuándo se le pide; distinta del vencimiento de la tarea."
                ),
                ("tax_year", VariableType.Number, false, null, "Año fiscal de la tarea, si tiene."),
                ("portal_link", VariableType.Url, true, null, "URL base del portal del cliente."),
                ("product_name", VariableType.String, true, null, "Nombre del producto en el asunto."),
            ]
        );

    // Plantilla propia de "te sumaron a una cita": versión explícita por encima de las anteriores.
    private const int AppointmentAttendeeAddedVersion = 26;

    // Botones de respuesta de una cita (RSVP). Solo salen si hay enlace: el organizador y las reuniones
    // sueltas no los llevan. Los enlaces abren una página que pide confirmar con un clic.
    private const string RsvpButtonsRows = """
              {% if rsvp_accept_link != blank %}
              <tr><td style="padding-top:20px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;font-weight:bold;color:#23384B;mso-line-height-rule:exactly;">Will you attend?</td></tr>
              <tr><td style="padding:10px 0 4px 0;"><table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr>
                <td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ rsvp_accept_link }}" target="_blank" style="display:inline-block;padding:11px 22px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Accept</a></td>
                <td width="8" style="font-size:0;line-height:0;">&nbsp;</td>
                <td align="center" style="border:1px solid #C9D6E2;border-radius:10px;"><a href="{{ rsvp_maybe_link }}" target="_blank" style="display:inline-block;padding:10px 20px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:18px;font-weight:bold;color:#1E466B;text-decoration:none;border-radius:10px;">Maybe</a></td>
                <td width="8" style="font-size:0;line-height:0;">&nbsp;</td>
                <td align="center" style="border:1px solid #C9D6E2;border-radius:10px;"><a href="{{ rsvp_decline_link }}" target="_blank" style="display:inline-block;padding:10px 20px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:18px;font-weight:bold;color:#1E466B;text-decoration:none;border-radius:10px;">Decline</a></td>
              </tr></table></td></tr>
              {% endif %}
""";

    // v27: botones Accept / Maybe / Decline (RSVP) en las citas y en la invitación de su sala.
    private const int RsvpButtonsVersion = 27;

    // Propiedad y no campo: los inicializadores estáticos corren en orden textual y MeetingInvitation va antes.
    private static IReadOnlyList<(string, VariableType, bool, string?, string?)> RsvpButtonsVariables =>
        [
            ("rsvp_accept_link", VariableType.Url, false, null, "Enlace para aceptar la cita (solo invitados)."),
            ("rsvp_maybe_link", VariableType.Url, false, null, "Enlace para responder tal vez."),
            ("rsvp_decline_link", VariableType.Url, false, null, "Enlace para rechazar la cita."),
        ];

    /// <summary>
    /// A quien suman a una cita ya agendada. Antes reusaba la de "te agendaron" y no decía que lo habían
    /// agregado a algo que ya existía (CAL-09 §2).
    /// </summary>
    private static NotificationTemplateSeed AppointmentAttendeeAdded { get; } =
        new(
            EventKey: "calendar.appointment_attendee_added.v1",
            TemplateKey: "calendar.appointment_attendee_added.v1",
            Name: "Calendar — Te sumaron a una cita",
            Subject: "{{ product_name }} — You were added to {{ appointment_title }}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Appointment</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:16px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">You're on the guest list</td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">The office added you to <strong style="color:#1E466B;">{{ appointment_title }}</strong>.</td></tr>
              <tr><td style="padding-bottom:12px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF" style="background-color:#EAF4FF;border-radius:10px;"><tr><td style="padding:14px 18px;border-left:3px solid #67BAF4;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:22px;color:#23384B;mso-line-height-rule:exactly;"><strong>{{ start_local }}</strong> <span style="color:#70869A;">({{ time_zone }})</span></td></tr></table></td></tr>
              {% if is_recurring %}<tr><td style="padding-bottom:4px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#496174;mso-line-height-rule:exactly;">It repeats: check the calendar for all dates.</td></tr>{% endif %}
              {% if is_virtual %}<tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#496174;mso-line-height-rule:exactly;">It's a video meeting; the link is in the appointment.</td></tr>{% endif %}
              <tr><td style="padding-top:14px;font-family:Arial,Helvetica,sans-serif;font-size:13px;line-height:20px;color:#70869A;mso-line-height-rule:exactly;">Someone at the office added you to this appointment after it was booked.</td></tr>
            """
                + RsvpButtonsRows
                + """
                </table>
                """,
            Variables:
            [
                ("appointment_title", VariableType.String, true, null, "Titulo de la cita."),
                ("start_local", VariableType.String, true, null, "Inicio en la zona de la cita, ya formateado."),
                ("time_zone", VariableType.String, true, null, "Zona de la cita, escrita al lado de la hora."),
                ("is_recurring", VariableType.Bool, false, null, "Si la cita se repite."),
                ("is_virtual", VariableType.Bool, false, null, "Si es una reunion por video."),
                ("portal_link", VariableType.Url, true, null, "URL base del portal."),
                ("product_name", VariableType.String, true, null, "Nombre del producto en el asunto."),
                .. RsvpButtonsVariables,
            ],
            ContentVersion: RsvpButtonsVersion
        );

    /// <summary>
    /// La invitación. La hora va con su zona escrita al lado: «10:00» a secas es exactamente lo que
    /// hace que alguien se presente con una hora de diferencia.
    /// </summary>
    private static NotificationTemplateSeed AppointmentScheduled { get; } =
        new(
            EventKey: "calendar.appointment_scheduled.v1",
            TemplateKey: "calendar.appointment_scheduled.v1",
            Name: "Calendar — Te agendaron una cita",
            Subject: "{{ product_name }} — {{ appointment_title }}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Appointment</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:16px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">You have a new appointment</td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">You've been scheduled for: <strong style="color:#1E466B;">{{ appointment_title }}</strong>.</td></tr>
              <tr><td style="padding-bottom:12px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF" style="background-color:#EAF4FF;border-radius:10px;"><tr><td style="padding:14px 18px;border-left:3px solid #67BAF4;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:22px;color:#23384B;mso-line-height-rule:exactly;"><strong>{{ start_local }}</strong> <span style="color:#70869A;">({{ time_zone }})</span></td></tr></table></td></tr>
              {% if is_recurring %}<tr><td style="padding-bottom:4px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#496174;mso-line-height-rule:exactly;">It repeats: check the calendar for all dates.</td></tr>{% endif %}
              {% if is_virtual %}<tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#496174;mso-line-height-rule:exactly;">It's a video meeting; the link is in the appointment.</td></tr>{% endif %}
            """
                + RsvpButtonsRows
                + """
                </table>
                """,
            Variables:
            [
                ("appointment_title", VariableType.String, true, null, "Titulo de la cita."),
                ("start_local", VariableType.String, true, null, "Inicio en la zona de la cita, ya formateado."),
                ("time_zone", VariableType.String, true, null, "Zona de la cita, escrita al lado de la hora."),
                ("is_recurring", VariableType.Bool, false, null, "Si la cita se repite."),
                ("is_virtual", VariableType.Bool, false, null, "Si es una reunion por video."),
                ("portal_link", VariableType.Url, true, null, "URL base del portal."),
                ("product_name", VariableType.String, true, null, "Nombre del producto en el asunto."),
                .. RsvpButtonsVariables,
            ],
            ContentVersion: RsvpButtonsVersion
        );

    /// <summary>
    /// El cambio de hora lleva la vieja y la nueva. Decir solo la nueva obliga a recordar cuál era, y
    /// quien tiene ocho citas esa semana no la recuerda.
    /// </summary>
    private static NotificationTemplateSeed AppointmentRescheduled { get; } =
        new(
            EventKey: "calendar.appointment_rescheduled.v1",
            TemplateKey: "calendar.appointment_rescheduled.v1",
            Name: "Calendar — Se movió tu cita",
            Subject: "{{ product_name }} — Your appointment moved",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Appointment</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:16px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Your appointment moved</td></tr>
              {% if previous_local %}<tr><td style="padding-bottom:10px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#70869A;mso-line-height-rule:exactly;">Before: <s>{{ previous_local }}</s></td></tr>{% endif %}
              <tr><td style="padding-bottom:4px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF" style="background-color:#EAF4FF;border-radius:10px;"><tr><td style="padding:14px 18px;border-left:3px solid #67BAF4;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:22px;color:#23384B;mso-line-height-rule:exactly;">Now: <strong>{{ new_local }}</strong> <span style="color:#70869A;">({{ time_zone }})</span></td></tr></table></td></tr>
            """
                + RsvpButtonsRows
                + """
                </table>
                """,
            Variables:
            [
                ("scope", VariableType.String, false, null, "Si se movio una ocurrencia o la serie."),
                ("previous_local", VariableType.String, false, null, "La hora vieja, para no obligar a recordarla."),
                ("new_local", VariableType.String, true, null, "La hora nueva, en la zona de la cita."),
                ("time_zone", VariableType.String, true, null, "Zona de la cita."),
                ("portal_link", VariableType.Url, true, null, "URL base del portal."),
                ("product_name", VariableType.String, true, null, "Nombre del producto en el asunto."),
                .. RsvpButtonsVariables,
            ],
            ContentVersion: RsvpButtonsVersion
        );

    // v28: calendar.appointment_updated.v1 nueva ("Edit details": título, descripción o lugar).
    private const int AppointmentUpdatedVersion = 28;

    /// <summary>
    /// Cambiaron título, descripción o lugar. El título viejo va tachado al lado: sin él nadie reconoce
    /// cuál de sus citas cambió.
    /// </summary>
    private static NotificationTemplateSeed AppointmentUpdated { get; } =
        new(
            EventKey: "calendar.appointment_updated.v1",
            TemplateKey: "calendar.appointment_updated.v1",
            Name: "Calendar — Cambiaron los detalles de tu cita",
            Subject: "{{ product_name }} — Updated: {{ appointment_title }}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Appointment</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:16px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Your appointment was updated</td></tr>
              {% if title_changed %}<tr><td style="padding-bottom:10px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#70869A;mso-line-height-rule:exactly;">Before: <s>{{ previous_title }}</s></td></tr>{% endif %}
              <tr><td style="padding-bottom:12px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF" style="background-color:#EAF4FF;border-radius:10px;"><tr><td style="padding:14px 18px;border-left:3px solid #67BAF4;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:22px;color:#23384B;mso-line-height-rule:exactly;"><strong>{{ appointment_title }}</strong><br /><span style="color:#496174;">{{ start_local }} <span style="color:#70869A;">({{ time_zone }})</span></span>{% if location != blank %}<br /><span style="color:#496174;">{% if location_changed %}New place: {% endif %}{{ location }}</span>{% endif %}</td></tr></table></td></tr>
              {% if description_changed and description != blank %}<tr><td style="padding-bottom:4px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#496174;mso-line-height-rule:exactly;white-space:pre-line;"><strong style="color:#23384B;">Notes:</strong> {{ description }}</td></tr>{% endif %}
              <tr><td style="padding-top:10px;font-family:Arial,Helvetica,sans-serif;font-size:13px;line-height:20px;color:#70869A;mso-line-height-rule:exactly;">The time didn't change, and your answer still stands.</td></tr>
            </table>
            """,
            Variables:
            [
                ("appointment_title", VariableType.String, true, null, "Titulo actual de la cita."),
                ("previous_title", VariableType.String, false, null, "Titulo anterior, solo si cambio."),
                ("title_changed", VariableType.Bool, false, null, "Si cambio el titulo."),
                ("location", VariableType.String, false, null, "Lugar actual (vacio = sin lugar)."),
                ("location_changed", VariableType.Bool, false, null, "Si cambio el lugar."),
                ("description", VariableType.String, false, null, "Notas actuales de la cita."),
                ("description_changed", VariableType.Bool, false, null, "Si cambiaron las notas."),
                ("start_local", VariableType.String, true, null, "Inicio en la zona de la cita, ya formateado."),
                ("time_zone", VariableType.String, true, null, "Zona de la cita."),
                ("portal_link", VariableType.Url, true, null, "URL base del portal."),
                ("product_name", VariableType.String, true, null, "Nombre del producto en el asunto."),
            ],
            ContentVersion: AppointmentUpdatedVersion
        );

    // v29: calendar.booking_declined.v1 nueva (la oficina rechaza una solicitud del portal).
    private const int BookingDeclinedVersion = 29;

    /// <summary>
    /// La oficina no pudo tomar la hora que pidió el cliente. Dice cuál era y por qué, y lo invita a
    /// pedir otra: un "rechazada" sin salida es un cliente que no vuelve.
    /// </summary>
    private static NotificationTemplateSeed BookingDeclined { get; } =
        new(
            EventKey: "calendar.booking_declined.v1",
            TemplateKey: "calendar.booking_declined.v1",
            Name: "Calendar — No se pudo confirmar tu cita",
            Subject: "{{ product_name }} — We couldn't confirm {{ appointment_title }}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Appointment request</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:16px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">We couldn't confirm your appointment</td></tr>
              <tr><td style="padding-bottom:12px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF" style="background-color:#EAF4FF;border-radius:10px;"><tr><td style="padding:14px 18px;border-left:3px solid #67BAF4;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:22px;color:#23384B;mso-line-height-rule:exactly;"><strong>{{ appointment_title }}</strong>{% if start_local != blank %}<br /><span style="color:#496174;"><s>{{ start_local }}</s> <span style="color:#70869A;">({{ time_zone }})</span></span>{% endif %}</td></tr></table></td></tr>
              {% if reason != blank %}<tr><td style="padding-bottom:10px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#496174;mso-line-height-rule:exactly;"><strong style="color:#23384B;">From your office:</strong> {{ reason }}</td></tr>{% endif %}
              <tr><td style="padding-top:6px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#496174;mso-line-height-rule:exactly;">You can pick another time from your portal: <a href="{{ portal_link }}" style="color:#1E466B;font-weight:bold;">Book another time</a></td></tr>
            </table>
            """,
            Variables:
            [
                ("appointment_title", VariableType.String, true, null, "Titulo de la cita pedida."),
                ("start_local", VariableType.String, false, null, "Hora pedida, en la zona de la cita."),
                ("time_zone", VariableType.String, true, null, "Zona de la cita."),
                ("reason", VariableType.String, false, null, "Motivo de la oficina (opcional)."),
                ("portal_link", VariableType.Url, true, null, "URL del portal del cliente."),
                ("product_name", VariableType.String, true, null, "Nombre del producto en el asunto."),
            ],
            ContentVersion: BookingDeclinedVersion
        );

    /// <summary>El aviso que más importa: no mandarlo deja a alguien presentándose a algo que no existe.</summary>
    private static NotificationTemplateSeed AppointmentCancelled { get; } =
        new(
            EventKey: "calendar.appointment_cancelled.v1",
            TemplateKey: "calendar.appointment_cancelled.v1",
            Name: "Calendar — Se canceló tu cita",
            Subject: "{{ product_name }} — Your appointment was cancelled",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Appointment</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Your appointment was cancelled</td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">An appointment you had scheduled was cancelled.</td></tr>
              {% if reason %}<tr><td style="padding-bottom:12px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#FDEEEE" style="background-color:#FDEEEE;border-radius:10px;"><tr><td style="padding:14px 18px;border-left:3px solid #D65B5B;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#23384B;mso-line-height-rule:exactly;">{{ reason }}</td></tr></table></td></tr>{% endif %}
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#496174;mso-line-height-rule:exactly;">No action is needed.</td></tr>
            </table>
            """,
            Variables:
            [
                ("scope", VariableType.String, false, null, "Si se cancelo una ocurrencia o la serie."),
                ("reason", VariableType.String, false, null, "Motivo, si el organizador lo escribio."),
                ("portal_link", VariableType.Url, true, null, "URL base del portal."),
                ("product_name", VariableType.String, true, null, "Nombre del producto en el asunto."),
            ]
        );

    private static NotificationTemplateSeed ClientRequestCreated { get; } =
        new(
            EventKey: "task.client_request_created.v1",
            TemplateKey: "task.client_request_created.v1",
            Name: "Task — Tu contador te pidió documentación",
            Subject: "{{ product_name }} — {{ request_title }}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Documents</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:16px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Your accountant requested documents</td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ customer_name }}</strong>, your accountant needs you to send:</td></tr>
              <tr><td style="padding-bottom:12px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF" style="background-color:#EAF4FF;border-radius:10px;"><tr><td style="padding:14px 18px;border-left:3px solid #67BAF4;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#23384B;mso-line-height-rule:exactly;"><strong>{{ request_title }}</strong>{% if request_details %}<br />{{ request_details }}{% endif %}</td></tr></table></td></tr>
              {% if due_at_utc %}
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#496174;mso-line-height-rule:exactly;">Please send it before <strong style="color:#23384B;">{{ due_at_utc }}</strong>.</td></tr>
              {% endif %}
              <tr>
                <td align="left" style="padding:24px 0 4px 0;">
                  <!--[if mso]>
                  <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ portal_link }}" style="height:46px;v-text-anchor:middle;width:200px;" arcsize="22%" strokecolor="#1E466B" fillcolor="#1E466B"><w:anchorlock/><center style="color:#FFFFFF;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">Upload in my portal</center></v:roundrect>
                  <![endif]-->
                  <!--[if !mso]><!-- -->
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ portal_link }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Upload in my portal</a></td></tr></table>
                  <!--<![endif]-->
                </td>
              </tr>
            </table>
            """,
            Variables:
            [
                ("customer_name", VariableType.String, true, null, "Nombre del cliente, del directorio local."),
                ("request_title", VariableType.String, true, null, "Qué se le pide, en el idioma del cliente."),
                ("request_details", VariableType.String, false, null, "Detalle opcional del pedido."),
                ("due_at_utc", VariableType.String, false, null, "La fecha que se le dio al cliente."),
                ("portal_link", VariableType.Url, true, null, "URL base del portal del cliente."),
                ("product_name", VariableType.String, true, null, "Nombre del producto en el asunto."),
            ]
        );

    /// <summary>
    /// El archivo del cliente no pasó el escaneo. <b>Aquí no entra el motivo técnico.</b> «Tiene un
    /// virus» no le dice al cliente qué hacer y filtra infraestructura; el preparador recibe el motivo
    /// real por otro canal.
    /// </summary>
    private static NotificationTemplateSeed ClientRequestDocumentRejected { get; } =
        new(
            EventKey: "task.client_request_document_rejected.v1",
            TemplateKey: "task.client_request_document_rejected.v1",
            Name: "Task — No pudimos procesar tu archivo",
            Subject: "{{ product_name }} — We couldn't process {{ document_name }}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Documents</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:16px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">We couldn't process your file</td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ customer_name }}</strong>, {{ client_message }}</td></tr>
              <tr><td style="padding-bottom:12px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#FFF6E9" style="background-color:#FFF6E9;border-radius:10px;"><tr><td style="padding:14px 18px;border-left:3px solid #E8A13A;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#23384B;mso-line-height-rule:exactly;">File: <strong>{{ document_name }}</strong><br />Request: {{ request_title }}</td></tr></table></td></tr>
              <tr>
                <td align="left" style="padding:12px 0 4px 0;">
                  <!--[if mso]>
                  <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ portal_link }}" style="height:46px;v-text-anchor:middle;width:190px;" arcsize="22%" strokecolor="#1E466B" fillcolor="#1E466B"><w:anchorlock/><center style="color:#FFFFFF;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">Upload it again</center></v:roundrect>
                  <![endif]-->
                  <!--[if !mso]><!-- -->
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ portal_link }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Upload it again</a></td></tr></table>
                  <!--<![endif]-->
                </td>
              </tr>
            </table>
            """,
            Variables:
            [
                ("customer_name", VariableType.String, true, null, "Nombre del cliente, del directorio local."),
                ("document_name", VariableType.String, true, null, "Nombre del archivo que subió."),
                ("request_title", VariableType.String, true, null, "A qué pedido corresponde."),
                (
                    "client_message",
                    VariableType.String,
                    true,
                    null,
                    "Mensaje accionable para el cliente. Nunca el motivo técnico del rechazo."
                ),
                ("portal_link", VariableType.Url, true, null, "URL base del portal del cliente."),
                ("product_name", VariableType.String, true, null, "Nombre del producto en el asunto."),
            ]
        );

    private static NotificationTemplateSeed FormAccessLink { get; } =
        new(
            EventKey: "task.form_access_link.v1",
            TemplateKey: "task.form_access_link.v1",
            Name: "Client Forms - External access link",
            Subject: "{% if language == 'Es' %}Formulario seguro: {{ form_title }}{% else %}Secure form: {{ form_title }}{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:16px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;color:#23384B;">{% if language == 'Es' %}Tu formulario seguro está listo{% else %}A secure form is ready{% endif %}</td></tr>
              {% if participant_name %}<tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;">{% if language == 'Es' %}Hola{% else %}Hi{% endif %} <strong>{{ participant_name }}</strong>,</td></tr>{% endif %}
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;">{% if language == 'Es' %}Tu oficina de impuestos te pidió completar <strong>{{ form_title }}</strong>. Antes de mostrarte cualquier información, verificaremos tu identidad por correo.{% else %}Your tax office asked you to complete <strong>{{ form_title }}</strong>. You will verify your identity by email before any information is shown.{% endif %}</td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:22px;color:#496174;">{% if language == 'Es' %}Este enlace vence el {{ expires_at }} UTC.{% else %}This link expires on {{ expires_at }} UTC.{% endif %}</td></tr>
              <tr><td style="padding-top:16px;"><a href="{{ access_link }}" style="display:inline-block;padding:14px 28px;background:#1E466B;color:#FFFFFF;text-decoration:none;border-radius:8px;font-family:Arial,Helvetica,sans-serif;font-weight:bold;">{% if language == 'Es' %}Abrir formulario seguro{% else %}Open secure form{% endif %}</a></td></tr>
            </table>
            """,
            Variables:
            [
                ("participant_name", VariableType.String, false, null, "Optional participant name."),
                ("form_title", VariableType.String, true, null, "Form request title."),
                ("access_link", VariableType.Url, true, null, "Public form access URL."),
                ("expires_at", VariableType.String, true, null, "Access link expiry in UTC."),
                Language,
            ],
            ContentVersion: ClientFormsBilingualVersion
        );

    private static NotificationTemplateSeed FormAccessCode { get; } =
        new(
            EventKey: "task.form_access_code.v1",
            TemplateKey: "task.form_access_code.v1",
            Name: "Client Forms - Verification code",
            Subject: "{% if language == 'Es' %}Tu código de verificación para {{ form_title }}{% else %}Your verification code for {{ form_title }}{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:16px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;color:#23384B;">{% if language == 'Es' %}Verifica tu identidad{% else %}Verify your identity{% endif %}</td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;">{% if language == 'Es' %}Usa este código de un solo uso para continuar con{% else %}Use this one-time code to continue with{% endif %} <strong>{{ form_title }}</strong>.</td></tr>
              <tr><td style="padding:18px 0;font-family:Arial,Helvetica,sans-serif;font-size:32px;line-height:38px;font-weight:bold;letter-spacing:6px;color:#1E466B;">{{ code }}</td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:22px;color:#496174;">{% if language == 'Es' %}El código vence el {{ expires_at }} UTC. Si no lo pediste, puedes ignorar este correo.{% else %}The code expires on {{ expires_at }} UTC. If you did not request it, you can ignore this email.{% endif %}</td></tr>
            </table>
            """,
            Variables:
            [
                ("code", VariableType.String, true, null, "Six-digit verification code."),
                ("form_title", VariableType.String, true, null, "Form request title."),
                ("expires_at", VariableType.String, true, null, "Code expiry in UTC."),
                Language,
            ],
            ContentVersion: ClientFormsBilingualVersion
        );

    private static NotificationTemplateSeed FormRequestAssigned { get; } =
        new(
            EventKey: "task.form_request_assigned.v1",
            TemplateKey: "task.form_request_assigned.v1",
            Name: "Client Forms - Form assigned",
            Subject: "{% if language == 'Es' %}Acción requerida: {{ request_title }}{% else %}Action needed: {{ request_title }}{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:16px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;color:#23384B;">{% if language == 'Es' %}Tienes un formulario por completar{% else %}A form is ready for you{% endif %}</td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;">{% if language == 'Es' %}Hola <strong>{{ customer_name }}</strong>, tu oficina de impuestos te pidió completar <strong>{{ request_title }}</strong>.{% else %}Hi <strong>{{ customer_name }}</strong>, your tax office asked you to complete <strong>{{ request_title }}</strong>.{% endif %}</td></tr>
              {% if has_message %}<tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:22px;color:#496174;">{% if language == 'Es' %}Tu oficina te dejó una nota. Inicia sesión para leerla.{% else %}Your office left you a note. Sign in to read it.{% endif %}</td></tr>{% endif %}
              {% if due_date %}<tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:22px;color:#496174;">{% if language == 'Es' %}Fecha límite:{% else %}Due:{% endif %} <strong>{{ due_date }}</strong></td></tr>{% endif %}
              <tr><td style="padding-top:16px;"><a href="{{ portal_link }}" style="display:inline-block;padding:14px 28px;background:#1E466B;color:#FFFFFF;text-decoration:none;border-radius:8px;font-family:Arial,Helvetica,sans-serif;font-weight:bold;">{% if language == 'Es' %}Abrir formulario{% else %}Open form{% endif %}</a></td></tr>
            </table>
            """,
            Variables:
            [
                ("customer_name", VariableType.String, true, null, "Customer display name."),
                ("request_title", VariableType.String, true, null, "Form request title."),
                ("has_message", VariableType.Bool, false, null, "Whether the office left a note."),
                ("due_date", VariableType.Date, false, null, "Optional due date."),
                ("portal_link", VariableType.Url, true, null, "Client form URL."),
                Language,
            ],
            ContentVersion: ClientFormsBilingualVersion
        );

    private static NotificationTemplateSeed FormRequestReturned { get; } =
        new(
            EventKey: "task.form_request_returned.v1",
            TemplateKey: "task.form_request_returned.v1",
            Name: "Client Forms - Changes requested",
            Subject: "{% if language == 'Es' %}Cambios solicitados: {{ request_title }}{% else %}Changes requested: {{ request_title }}{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:16px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;color:#23384B;">{% if language == 'Es' %}Hace falta hacer cambios{% else %}Changes are needed{% endif %}</td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;">{% if language == 'Es' %}Hola <strong>{{ customer_name }}</strong>, tu oficina pidió cambios en {{ comment_count }} punto(s) de <strong>{{ request_title }}</strong>. Inicia sesión para ver sus notas.{% else %}Hi <strong>{{ customer_name }}</strong>, your office asked for changes on {{ comment_count }} item(s) in <strong>{{ request_title }}</strong>. Sign in to see their notes.{% endif %}</td></tr>
              <tr><td style="padding-top:16px;"><a href="{{ portal_link }}" style="display:inline-block;padding:14px 28px;background:#1E466B;color:#FFFFFF;text-decoration:none;border-radius:8px;font-family:Arial,Helvetica,sans-serif;font-weight:bold;">{% if language == 'Es' %}Abrir formulario{% else %}Open form{% endif %}</a></td></tr>
            </table>
            """,
            Variables:
            [
                ("customer_name", VariableType.String, true, null, "Customer display name."),
                ("request_title", VariableType.String, true, null, "Form request title."),
                ("comment_count", VariableType.Number, true, null, "Number of field comments."),
                ("due_date", VariableType.Date, false, null, "Optional due date."),
                ("portal_link", VariableType.Url, true, null, "Client form URL."),
                Language,
            ],
            ContentVersion: ClientFormsBilingualVersion
        );

    private static NotificationTemplateSeed FormRequestCompleted { get; } =
        new(
            EventKey: "task.form_request_completed.v1",
            TemplateKey: "task.form_request_completed.v1",
            Name: "Client Forms - Form completed",
            Subject: "{% if language == 'Es' %}Recibido: {{ request_title }}{% else %}Received: {{ request_title }}{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:16px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;color:#23384B;">{% if language == 'Es' %}Recibimos tu formulario{% else %}Your form was received{% endif %}</td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;">{% if language == 'Es' %}Hola <strong>{{ customer_name }}</strong>, tu oficina marcó <strong>{{ request_title }}</strong> como completado. Gracias.{% else %}Hi <strong>{{ customer_name }}</strong>, your office marked <strong>{{ request_title }}</strong> as complete. Thank you.{% endif %}</td></tr>
              <tr><td style="padding-top:16px;"><a href="{{ portal_link }}" style="display:inline-block;padding:14px 28px;background:#1E466B;color:#FFFFFF;text-decoration:none;border-radius:8px;font-family:Arial,Helvetica,sans-serif;font-weight:bold;">{% if language == 'Es' %}Abrir formulario{% else %}Open form{% endif %}</a></td></tr>
            </table>
            """,
            Variables:
            [
                ("customer_name", VariableType.String, true, null, "Customer display name."),
                ("request_title", VariableType.String, true, null, "Form request title."),
                ("portal_link", VariableType.Url, true, null, "Client form URL."),
                Language,
            ],
            ContentVersion: ClientFormsBilingualVersion
        );

    private static NotificationTemplateSeed FormAttachmentRejected { get; } =
        new(
            EventKey: "task.form_attachment_rejected.v1",
            TemplateKey: "task.form_attachment_rejected.v1",
            Name: "Client Forms - File rejected",
            Subject: "{% if language == 'Es' %}No pudimos aceptar un archivo que subiste{% else %}A file you uploaded couldn't be accepted{% endif %}",
            Html: $$$"""
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:16px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;color:#23384B;">{% if language == 'Es' %}Vuelve a subir un archivo{% else %}Please upload a file again{% endif %}</td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;">{% if language == 'Es' %}Hola <strong>{{ customer_name }}</strong>, no pudimos aceptar uno de tus archivos (.{{ file_extension }}) de <strong>{{ request_title }}</strong>.{% else %}Hi <strong>{{ customer_name }}</strong>, one of your files (.{{ file_extension }}) for <strong>{{ request_title }}</strong> couldn't be accepted.{% endif %}</td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:22px;color:#496174;">{{{RejectionMessage}}}</td></tr>
              <tr><td style="padding-top:16px;"><a href="{{ portal_link }}" style="display:inline-block;padding:14px 28px;background:#1E466B;color:#FFFFFF;text-decoration:none;border-radius:8px;font-family:Arial,Helvetica,sans-serif;font-weight:bold;">{% if language == 'Es' %}Volver a subir{% else %}Upload again{% endif %}</a></td></tr>
            </table>
            """,
            Variables:
            [
                ("customer_name", VariableType.String, true, null, "Customer display name."),
                ("request_title", VariableType.String, true, null, "Form request title."),
                ("file_extension", VariableType.String, true, null, "Safe file extension only."),
                ("client_message", VariableType.String, true, null, "Fixed message selected from the rejection code."),
                ("client_message_es", VariableType.String, false, null, "The same fixed message in Spanish."),
                ("portal_link", VariableType.Url, true, null, "Client form URL."),
                Language,
            ],
            ContentVersion: ClientFormsBilingualVersion
        );

    private static NotificationTemplateSeed FormRequestReminder { get; } =
        new(
            EventKey: "task.form_request_reminder.v1",
            TemplateKey: "task.form_request_reminder.v1",
            Name: "Client Forms - Form reminder",
            Subject: "{% if language == 'Es' %}Recordatorio: {{ request_title }}{% else %}Reminder: {{ request_title }}{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:16px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;color:#23384B;">{% if language == 'Es' %}{% if is_overdue %}Este formulario está vencido{% else %}Un formulario te espera{% endif %}{% else %}{% if is_overdue %}This form is overdue{% else %}A form is waiting for you{% endif %}{% endif %}</td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;">{% if language == 'Es' %}Hola <strong>{{ customer_name }}</strong>, completa <strong>{{ request_title }}</strong>, por favor.{% else %}Hi <strong>{{ customer_name }}</strong>, please complete <strong>{{ request_title }}</strong>.{% endif %}</td></tr>
              {% if due_date %}<tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:22px;color:#496174;">{% if language == 'Es' %}Fecha límite:{% else %}Due:{% endif %} <strong>{{ due_date }}</strong></td></tr>{% endif %}
              <tr><td style="padding-top:16px;"><a href="{{ portal_link }}" style="display:inline-block;padding:14px 28px;background:#1E466B;color:#FFFFFF;text-decoration:none;border-radius:8px;font-family:Arial,Helvetica,sans-serif;font-weight:bold;">{% if language == 'Es' %}Abrir formulario{% else %}Open form{% endif %}</a></td></tr>
            </table>
            """,
            Variables:
            [
                ("customer_name", VariableType.String, true, null, "Customer display name."),
                ("request_title", VariableType.String, true, null, "Form request title."),
                ("due_date", VariableType.Date, false, null, "Optional due date."),
                ("is_overdue", VariableType.Bool, true, null, "Whether the form is overdue."),
                ("portal_link", VariableType.Url, true, null, "Client form URL."),
                Language,
            ],
            ContentVersion: ClientFormsBilingualVersion
        );

    // Avisos a un participante externo sin vincular (P-14.7). No hay portal ni enlace: el token se guarda
    // hasheado, así que se le remite al enlace del correo de acceso o a pedirle a la oficina que lo reenvíe.
    private static NotificationTemplateSeed FormParticipantReturned { get; } =
        new(
            EventKey: "task.form_participant_returned.v1",
            TemplateKey: "task.form_participant_returned.v1",
            Name: "Client Forms - Changes requested (external)",
            Subject: "{% if language == 'Es' %}Cambios solicitados: {{ form_title }}{% else %}Changes requested: {{ form_title }}{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:16px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;color:#23384B;">{% if language == 'Es' %}Hace falta hacer cambios{% else %}Changes are needed{% endif %}</td></tr>
              {% if participant_name %}<tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;">{% if language == 'Es' %}Hola{% else %}Hi{% endif %} <strong>{{ participant_name }}</strong>,</td></tr>{% endif %}
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;">{% if language == 'Es' %}Tu oficina de impuestos pidió cambios en {{ comment_count }} punto(s) de <strong>{{ form_title }}</strong>.{% else %}Your tax office asked for changes on {{ comment_count }} item(s) in <strong>{{ form_title }}</strong>.{% endif %}</td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:22px;color:#496174;">{% if language == 'Es' %}Abre el enlace seguro del correo que te enviamos antes para leer sus notas. Si el enlace venció, pide a tu oficina que te envíe uno nuevo.{% else %}Open the secure link from the email we sent you earlier to read their notes. If the link expired, ask your office to send you a new one.{% endif %}</td></tr>
            </table>
            """,
            Variables:
            [
                ("participant_name", VariableType.String, false, null, "Optional participant name."),
                ("form_title", VariableType.String, true, null, "Form request title."),
                ("comment_count", VariableType.Number, true, null, "Number of field comments."),
                Language,
            ],
            ContentVersion: ClientFormsBilingualVersion
        );

    private static NotificationTemplateSeed FormParticipantCompleted { get; } =
        new(
            EventKey: "task.form_participant_completed.v1",
            TemplateKey: "task.form_participant_completed.v1",
            Name: "Client Forms - Form completed (external)",
            Subject: "{% if language == 'Es' %}Recibido: {{ form_title }}{% else %}Received: {{ form_title }}{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:16px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;color:#23384B;">{% if language == 'Es' %}Recibimos tu formulario{% else %}Your form was received{% endif %}</td></tr>
              {% if participant_name %}<tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;">{% if language == 'Es' %}Hola{% else %}Hi{% endif %} <strong>{{ participant_name }}</strong>,</td></tr>{% endif %}
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;">{% if language == 'Es' %}Tu oficina de impuestos marcó <strong>{{ form_title }}</strong> como completado. Gracias. No necesitas hacer nada más.{% else %}Your tax office marked <strong>{{ form_title }}</strong> as complete. Thank you. You don't need to do anything else.{% endif %}</td></tr>
            </table>
            """,
            Variables:
            [
                ("participant_name", VariableType.String, false, null, "Optional participant name."),
                ("form_title", VariableType.String, true, null, "Form request title."),
                Language,
            ],
            ContentVersion: ClientFormsBilingualVersion
        );

    private static NotificationTemplateSeed FormParticipantReminder { get; } =
        new(
            EventKey: "task.form_participant_reminder.v1",
            TemplateKey: "task.form_participant_reminder.v1",
            Name: "Client Forms - Form reminder (external)",
            Subject: "{% if language == 'Es' %}Recordatorio: {{ form_title }}{% else %}Reminder: {{ form_title }}{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:16px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;color:#23384B;">{% if language == 'Es' %}{% if is_overdue %}Este formulario está vencido{% else %}Un formulario te espera{% endif %}{% else %}{% if is_overdue %}This form is overdue{% else %}A form is waiting for you{% endif %}{% endif %}</td></tr>
              {% if participant_name %}<tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;">{% if language == 'Es' %}Hola{% else %}Hi{% endif %} <strong>{{ participant_name }}</strong>,</td></tr>{% endif %}
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;">{% if language == 'Es' %}Abre el enlace seguro del correo anterior para completar{% else %}Open the secure link from the earlier email to complete{% endif %} <strong>{{ form_title }}</strong>.</td></tr>
            </table>
            """,
            Variables:
            [
                ("participant_name", VariableType.String, false, null, "Optional participant name."),
                ("form_title", VariableType.String, true, null, "Form request title."),
                ("is_overdue", VariableType.Bool, true, null, "Whether the form is overdue."),
                ("language", VariableType.String, true, "En", "Recipient language: Es or En."),
            ],
            // v2: tildes en el español. v3: preheader bilingüe.
            ContentVersion: 3
        );

    private static NotificationTemplateSeed FormRequestSubmitted { get; } =
        new(
            EventKey: "task.form_request_submitted.v1",
            TemplateKey: "task.form_request_submitted.v1",
            Name: "Client Forms - Form submitted",
            Subject: "Form ready for review",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:16px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;color:#23384B;">A form is ready for review</td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;"><strong>{{ customer_name }}</strong> submitted <strong>{{ request_title }}</strong>.</td></tr>
              <tr><td style="padding-top:16px;"><a href="{{ staff_link }}" style="display:inline-block;padding:14px 28px;background:#1E466B;color:#FFFFFF;text-decoration:none;border-radius:8px;font-family:Arial,Helvetica,sans-serif;font-weight:bold;">Review form</a></td></tr>
            </table>
            """,
            Variables:
            [
                ("customer_name", VariableType.String, true, null, "Customer or participant display name."),
                ("request_title", VariableType.String, true, null, "Form request title."),
                ("revision_number", VariableType.Number, true, null, "Submission revision number."),
                ("staff_link", VariableType.Url, true, null, "Staff form inbox URL."),
                ("language", VariableType.String, true, "En", "Recipient language: Es or En."),
            ],
            ContentVersion: 1
        );

    private static NotificationTemplateSeed FormActionFailed { get; } =
        new(
            EventKey: "task.form_action_failed.v1",
            TemplateKey: "task.form_action_failed.v1",
            Name: "Client Forms - Action failed",
            Subject: "A form action needs attention",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:16px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;color:#23384B;">A form action needs attention</td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;">The action <strong>{{ action_type }}</strong> could not finish. Error: {{ error_code }}.</td></tr>
              <tr><td style="padding-top:16px;"><a href="{{ staff_link }}" style="display:inline-block;padding:14px 28px;background:#1E466B;color:#FFFFFF;text-decoration:none;border-radius:8px;font-family:Arial,Helvetica,sans-serif;font-weight:bold;">Review action</a></td></tr>
            </table>
            """,
            Variables:
            [
                ("action_type", VariableType.String, true, null, "Action type."),
                ("error_code", VariableType.String, true, null, "Safe error code."),
                ("staff_link", VariableType.Url, true, null, "Staff form inbox URL."),
                ("language", VariableType.String, true, "En", "Recipient language: Es or En."),
            ],
            ContentVersion: 1
        );

    private static NotificationTemplateSeed FormParticipantCancelled { get; } =
        new(
            EventKey: "task.form_participant_cancelled.v1",
            TemplateKey: "task.form_participant_cancelled.v1",
            Name: "Client Forms - Form cancelled (external)",
            Subject: "{% if language == 'Es' %}Cancelado: {{ form_title }}{% else %}Cancelled: {{ form_title }}{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:16px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;color:#23384B;">{% if language == 'Es' %}Se canceló esta solicitud{% else %}This form request was cancelled{% endif %}</td></tr>
              {% if participant_name %}<tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;">{% if language == 'Es' %}Hola{% else %}Hi{% endif %} <strong>{{ participant_name }}</strong>,</td></tr>{% endif %}
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;">{% if language == 'Es' %}Tu oficina de impuestos canceló <strong>{{ form_title }}</strong>. El enlace que recibiste ya no funciona y no necesitas hacer nada más. Si crees que es un error, comunícate con tu oficina.{% else %}Your tax office cancelled <strong>{{ form_title }}</strong>. The link you received no longer works, and you don't need to do anything else. If you think this is a mistake, contact your office.{% endif %}</td></tr>
            </table>
            """,
            Variables:
            [
                ("participant_name", VariableType.String, false, null, "Optional participant name."),
                ("form_title", VariableType.String, true, null, "Form request title."),
                Language,
            ],
            ContentVersion: ClientFormsBilingualVersion
        );

    private static NotificationTemplateSeed FormParticipantAttachmentRejected { get; } =
        new(
            EventKey: "task.form_participant_attachment_rejected.v1",
            TemplateKey: "task.form_participant_attachment_rejected.v1",
            Name: "Client Forms - File rejected (external)",
            Subject: "{% if language == 'Es' %}No pudimos aceptar un archivo que subiste{% else %}A file you uploaded couldn't be accepted{% endif %}",
            Html: $$$"""
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:16px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;color:#23384B;">{% if language == 'Es' %}Vuelve a subir un archivo{% else %}Please upload a file again{% endif %}</td></tr>
              {% if participant_name %}<tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;">{% if language == 'Es' %}Hola{% else %}Hi{% endif %} <strong>{{ participant_name }}</strong>,</td></tr>{% endif %}
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;">{% if language == 'Es' %}No pudimos aceptar uno de tus archivos (.{{ file_extension }}) de <strong>{{ form_title }}</strong>.{% else %}One of your files (.{{ file_extension }}) for <strong>{{ form_title }}</strong> couldn't be accepted.{% endif %}</td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:22px;color:#496174;">{{{RejectionMessage}}}</td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:22px;color:#496174;">{% if language == 'Es' %}Abre el enlace seguro del correo que te enviamos antes para volver a subirlo. Si el enlace venció, pide a tu oficina que te envíe uno nuevo.{% else %}Open the secure link from the email we sent you earlier to upload it again. If the link expired, ask your office to send you a new one.{% endif %}</td></tr>
            </table>
            """,
            Variables:
            [
                ("participant_name", VariableType.String, false, null, "Optional participant name."),
                ("form_title", VariableType.String, true, null, "Form request title."),
                ("file_extension", VariableType.String, true, null, "Safe file extension only."),
                ("client_message", VariableType.String, true, null, "Fixed message selected from the rejection code."),
                ("client_message_es", VariableType.String, false, null, "The same fixed message in Spanish."),
                Language,
            ],
            ContentVersion: ClientFormsBilingualVersion
        );

    /// <summary>
    /// La factura que la OFICINA manda a su cliente. Primera plantilla sobre <c>tenant-base</c>: el
    /// correo es de la oficina, no de la plataforma, así que lleva su cáscara y su logo.
    ///
    /// <para>Antes no existía — el CRM componía el HTML a mano y lo mandaba a
    /// <c>/notifications/email/send</c>, por eso llegaba sin marca ni formato.</para>
    /// </summary>
    private static NotificationTemplateSeed InvoiceSent { get; } =
        new(
            EventKey: "billing.invoice_sent.v1",
            TemplateKey: "billing.invoice_sent",
            Name: "Billing — Factura enviada al cliente",
            Subject: "Invoice {{ invoice_number }} from {{ tenant_name }}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Invoice</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Invoice {{ invoice_number }}</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ customer_name }}</strong>, here is your invoice from {{ tenant_name }}.{% if has_pdf %} A PDF copy is attached.{% endif %}</td></tr>
              <tr>
                <td style="padding:18px 0 4px 0;">
                  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="border:1px solid #E1E8EE;border-radius:10px;">
                    <tr>
                      <td style="padding:16px 18px;font-family:Arial,Helvetica,sans-serif;font-size:13px;line-height:20px;color:#70869A;">Amount due</td>
                      <td align="right" style="padding:16px 18px;font-family:Arial,Helvetica,sans-serif;font-size:20px;line-height:26px;font-weight:bold;color:#23384B;">{{ amount_due }}</td>
                    </tr>
                    {% if due_date %}
                    <tr>
                      <td style="padding:0 18px 16px 18px;font-family:Arial,Helvetica,sans-serif;font-size:13px;line-height:20px;color:#70869A;">Due date</td>
                      <td align="right" style="padding:0 18px 16px 18px;font-family:Arial,Helvetica,sans-serif;font-size:13px;line-height:20px;color:#496174;">{{ due_date }}</td>
                    </tr>
                    {% endif %}
                  </table>
                </td>
              </tr>
              {% if payment_link %}
              <tr>
                <td align="left" style="padding:26px 0 4px 0;">
                  <!--[if mso]>
                  <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ payment_link }}" style="height:46px;v-text-anchor:middle;width:200px;" arcsize="22%" strokecolor="#1E466B" fillcolor="#1E466B"><w:anchorlock/><center style="color:#FFFFFF;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">Pay online</center></v:roundrect>
                  <![endif]-->
                  <!--[if !mso]><!-- -->
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ payment_link }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Pay online</a></td></tr></table>
                  <!--<![endif]-->
                </td>
              </tr>
              {% endif %}
              <tr><td style="padding:22px 0 0 0;font-family:Arial,Helvetica,sans-serif;font-size:13px;line-height:20px;color:#70869A;mso-line-height-rule:exactly;">Thank you for your business.</td></tr>
            </table>
            """,
            Variables:
            [
                ("invoice_number", VariableType.String, true, null, "Numero de la factura (INV-2026-00005)."),
                ("customer_name", VariableType.String, true, null, "Nombre del cliente que recibe."),
                ("tenant_name", VariableType.String, false, null, "Lo inyecta el renderer desde la proyeccion."),
                ("amount_due", VariableType.String, true, null, "Importe pendiente ya formateado con su moneda."),
                (
                    "due_date",
                    VariableType.String,
                    false,
                    null,
                    "Fecha de vencimiento ya formateada. Vacia = no se muestra."
                ),
                ("payment_link", VariableType.Url, false, null, "URL estable de pago. Vacia = no se muestra el boton."),
                ("has_pdf", VariableType.Bool, false, null, "Falso = no se promete un adjunto que no va."),
                ("preheader", VariableType.String, false, "Your invoice is ready.", "Linea de vista previa."),
            ],
            LayoutKey: "tenant-base"
        );
}
