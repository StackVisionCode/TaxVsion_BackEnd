using TaxVision.Scribe.Domain.Templates;

namespace TaxVision.Scribe.Application.Templates.Seed;

/// <summary>
/// Versión en español de las plantillas que salían solo en inglés. Con <c>language = "Es"</c> salen asunto, cuerpo y
/// preheader en español; sin <c>language</c> salen en inglés, byte a byte igual que antes. El español se deriva del
/// HTML inglés frase por frase: si una frase ya no existe (alguien cambió el inglés), falla al cargar y las pruebas lo
/// ven. Las plantillas personalizadas de una oficina no pasan por aquí.
/// </summary>
public static partial class NotificationTemplateSeedSource
{
    /// <summary>v37 (por plantilla): plantillas en el idioma de quien lee (cliente, staff, citas, cuenta y cobros).</summary>
    private const int RecipientLanguageVersion = 37;

    private sealed record SpanishVersion(
        string? Subject,
        string? Preheader,
        IReadOnlyList<(string English, string Spanish)> Phrases
    );

    private static NotificationTemplateSeed WithSpanish(NotificationTemplateSeed seed)
    {
        if (!SpanishVersions.TryGetValue(seed.TemplateKey, out var spanish))
            return seed;

        return seed with
        {
            Subject = spanish.Subject is null ? seed.Subject : Bilingual(spanish.Subject, seed.Subject),
            Html = Bilingual(Translate(seed.TemplateKey, seed.Html, spanish.Phrases), seed.Html),
            Variables = WithLanguageVariable(seed.Variables, spanish.Preheader),
            ContentVersion = Math.Max(seed.ContentVersion, RecipientLanguageVersion),
        };
    }

    private static string Translate(
        string templateKey,
        string english,
        IReadOnlyList<(string English, string Spanish)> phrases
    )
    {
        var spanish = english;
        foreach (var (from, to) in phrases)
        {
            if (!spanish.Contains(from, StringComparison.Ordinal))
                throw new InvalidOperationException($"{templateKey}: the Spanish version expects '{from}'.");
            spanish = spanish.Replace(from, to, StringComparison.Ordinal);
        }
        return spanish;
    }

    // Declara language si falta y, si la plantilla trae su propio preheader, le pone la rama en español.
    private static IReadOnlyList<(
        string Name,
        VariableType Type,
        bool Required,
        string? DefaultValue,
        string? Description
    )> WithLanguageVariable(
        IReadOnlyList<(
            string Name,
            VariableType Type,
            bool Required,
            string? DefaultValue,
            string? Description
        )> variables,
        string? spanishPreheader
    )
    {
        var result = variables
            .Select(v =>
                v.Name == "preheader" && spanishPreheader is not null && v.DefaultValue is not null
                    ? (v.Name, v.Type, v.Required, Bilingual(spanishPreheader, v.DefaultValue), v.Description)
                    : v
            )
            .ToList();
        if (!result.Exists(v => v.Name == "language"))
            result.Add(Language);
        return result;
    }

    // ---------- Piezas repetidas ----------

    private static (string, string) ButtonFallbackEs =>
        (
            "Button not working? Copy this link into your browser:",
            "¿El botón no funciona? Copia este enlace en tu navegador:"
        );

    private static IReadOnlyList<(string, string)> MeetingDetailsEs =>
        [
            (
                """<strong style="color:#23384B;">When:</strong>""",
                """<strong style="color:#23384B;">Cuándo:</strong>"""
            ),
            ("Host:</strong>", "Anfitrión:</strong>"),
            ("Meeting code:</strong>", "Código de la reunión:</strong>"),
        ];

    private static IReadOnlyList<(string, string)> RsvpEs =>
        [
            ("Will you attend?", "¿Vas a asistir?"),
            (">Accept</a>", ">Aceptar</a>"),
            (">Maybe</a>", ">Tal vez</a>"),
            (">Decline</a>", ">Rechazar</a>"),
        ];

    private static (string, string) WalletAudienceEs =>
        (
            "You're receiving this because you can manage your office's Wallet on {{ product_name }}.",
            "Recibes este correo porque puedes administrar el Wallet de tu oficina en {{ product_name }}."
        );

    // office llega con "your office" cuando Auth no conoce la oficina.
    private const string OfficeEs = "{% if office == 'your office' %}tu oficina{% else %}{{ office }}{% endif %}";

    // cadence llega como código: month o year.
    private const string CadenceEveryEs = "{% if cadence == 'year' %}cada año{% else %}cada mes{% endif %}";
    private const string CadenceAdjectiveEs = "{% if cadence == 'year' %}anual{% else %}mensual{% endif %}";

    private static readonly IReadOnlyDictionary<string, SpanishVersion> SpanishVersions = new Dictionary<
        string,
        SpanishVersion
    >(StringComparer.Ordinal)
    {
        // ---------- Al cliente ----------
        ["task.waiting_on_client.v1"] = new(
            "{{ product_name }} — Nos faltan documentos tuyos",
            "Todavía nos faltan algunos documentos tuyos para seguir adelante.",
            [
                (">Documents<", ">Documentos<"),
                ("We're missing some documents", "Nos faltan algunos documentos"),
                (
                    """Hi <strong style="color:#23384B;">{{ customer_name }}</strong>, to continue with""",
                    """Hola, <strong style="color:#23384B;">{{ customer_name }}</strong>. Para continuar con"""
                ),
                ("(tax year {{ tax_year }})", "(año fiscal {{ tax_year }})"),
                (" we need you to send us:", " necesitamos que nos envíes:"),
                ("Please send it by", "Envíalo a más tardar el"),
                ("Upload documents", "Subir documentos"),
            ]
        ),
        ["task.client_request_created.v1"] = new(
            null,
            "Tu preparador te pidió algunos documentos. Esto es lo que necesita.",
            [
                (">Documents<", ">Documentos<"),
                ("Your accountant requested documents", "Tu contador te pidió documentos"),
                (
                    """Hi <strong style="color:#23384B;">{{ customer_name }}</strong>, your accountant needs you to send:""",
                    """Hola, <strong style="color:#23384B;">{{ customer_name }}</strong>. Tu contador necesita que le envíes:"""
                ),
                ("Please send it by", "Envíalo a más tardar el"),
                ("Upload in my portal", "Subir en mi portal"),
            ]
        ),
        ["task.client_request_document_rejected.v1"] = new(
            "{{ product_name }} — No pudimos procesar {{ document_name }}",
            "No pudimos procesar un archivo que subiste. Inténtalo de nuevo.",
            [
                (">Documents<", ">Documentos<"),
                ("We couldn't process your file", "No pudimos procesar tu archivo"),
                (
                    """Hi <strong style="color:#23384B;">{{ customer_name }}</strong>,""",
                    """Hola, <strong style="color:#23384B;">{{ customer_name }}</strong>,"""
                ),
                ("File: <strong>", "Archivo: <strong>"),
                ("<br />Request: ", "<br />Solicitud: "),
                ("Upload it again", "Volver a subirlo"),
            ]
        ),
        ["billing.invoice_sent"] = new(
            "Factura {{ invoice_number }} de {{ tenant_name }}",
            "Tu factura está lista.",
            [
                (">Invoice<", ">Factura<"),
                (">Invoice {{ invoice_number }}<", ">Factura {{ invoice_number }}<"),
                (
                    """Hi <strong style="color:#23384B;">{{ customer_name }}</strong>, here is your invoice from {{ tenant_name }}.""",
                    """Hola, <strong style="color:#23384B;">{{ customer_name }}</strong>. Aquí tienes tu factura de {{ tenant_name }}."""
                ),
                (" A PDF copy is attached.", " Adjuntamos una copia en PDF."),
                (">Amount due<", ">Importe a pagar<"),
                (">Due date<", ">Fecha de vencimiento<"),
                ("Pay online", "Pagar en línea"),
                ("Thank you for your business.", "Gracias por tu confianza."),
            ]
        ),

        // ---------- Trabajo del staff ----------
        ["reminder.due"] = new(
            null,
            "Un recordatorio rápido sobre algo de tu lista.",
            [
                (">Reminder<", ">Recordatorio<"),
                (
                    "Snoozed {{ snooze_count }} time(s).",
                    "Pospuesto {{ snooze_count }} {% if snooze_count == 1 %}vez{% else %}veces{% endif %}."
                ),
                ("Open {{ product_name }}", "Abrir {{ product_name }}"),
            ]
        ),
        ["task.form_request_submitted.v1"] = new(
            "Formulario listo para revisión",
            null,
            [
                ("A form is ready for review", "Un formulario está listo para revisión"),
                ("</strong> submitted <strong>", "</strong> envió <strong>"),
                ("Review form", "Revisar formulario"),
            ]
        ),
        ["task.form_action_failed.v1"] = new(
            "Una acción de formulario requiere atención",
            null,
            [
                ("A form action needs attention", "Una acción de formulario requiere atención"),
                (
                    "The action <strong>{{ action_type }}</strong> could not finish. Error: {{ error_code }}.",
                    "La acción <strong>{{ action_type }}</strong> no pudo terminar. Error: {{ error_code }}."
                ),
                ("Review action", "Revisar acción"),
            ]
        ),
        ["tenant.wisp_review_due.v1"] = new(
            "{% if is_overdue %}La revisión de tu plan de seguridad está vencida{% else %}La revisión de tu plan de seguridad vence el {{ due_date }}{% endif %}",
            "La revisión anual de tu plan de seguridad (WISP) requiere atención. Márcala como hecha en Office settings.",
            [
                (">Compliance<", ">Cumplimiento<"),
                ("Your security plan review is overdue", "La revisión de tu plan de seguridad está vencida"),
                ("Your security plan review is due soon", "La revisión de tu plan de seguridad vence pronto"),
                (
                    "The annual review of your office's written information security plan (WISP) was due on",
                    "La revisión anual del plan escrito de seguridad de la información (WISP) de tu oficina venció el"
                ),
                (
                    "The annual review of your office's written information security plan (WISP) is due on",
                    "La revisión anual del plan escrito de seguridad de la información (WISP) de tu oficina vence el"
                ),
                (
                    " The FTC Safeguards Rule requires reviewing the WISP at least once a year.",
                    " La FTC Safeguards Rule exige revisar el WISP al menos una vez al año."
                ),
                ("Once you've reviewed it, open", "Cuando lo hayas revisado, abre"),
                ("</strong> and mark it reviewed.", "</strong> y márcalo como revisado."),
                ("Open Compliance settings", "Abrir Compliance"),
                (
                    "You're receiving this because you're the security coordinator or an administrator of your office on {{ product_name }}.",
                    "Recibes este correo porque eres el responsable de seguridad o administrador de tu oficina en {{ product_name }}."
                ),
            ]
        ),
        ["auth.user_offboarding_incomplete.v1"] = new(
            "El retiro de {{ user_name }} no terminó",
            "Una parte de un retiro todavía no se confirmó. Puede que siga activo algo de su trabajo o acceso.",
            [
                (">Team<", ">Equipo<"),
                ("An offboarding didn't finish", "Un retiro no terminó"),
                (
                    "</strong> was offboarded, but {% if pending_modules.size == 1 %}this area hasn't{% else %}these areas haven't{% endif %} confirmed the handover yet:",
                    "</strong> ya no forma parte de la oficina, pero {% if pending_modules.size == 1 %}esta área todavía no confirmó{% else %}estas áreas todavía no confirmaron{% endif %} la entrega:"
                ),
                (
                    "Some of their work or access there may still be active. Review it, and contact support if it doesn't clear up.",
                    "Puede que parte de su trabajo o de su acceso siga activo ahí. Revísalo y contacta a soporte si no se resuelve."
                ),
                ("Open Users", "Abrir Users"),
                (
                    "You're receiving this because you manage your office's users on {{ product_name }}.",
                    "Recibes este correo porque administras los usuarios de tu oficina en {{ product_name }}."
                ),
            ]
        ),
        ["tenant.security_coordinator_vacated.v1"] = new(
            "Tu oficina necesita un nuevo responsable de seguridad",
            "Tu responsable de seguridad ya no está en tu oficina. Elige uno nuevo en Office settings.",
            [
                (">Compliance<", ">Cumplimiento<"),
                (
                    "Your office needs a new security coordinator",
                    "Tu oficina necesita un nuevo responsable de seguridad"
                ),
                (
                    "The person designated as your office's security coordinator is no longer with your office, so the role is now vacant.",
                    "La persona designada como responsable de seguridad ya no está en tu oficina, así que el puesto quedó vacante."
                ),
                (
                    "The FTC Safeguards Rule requires every office to designate someone to oversee its information security plan (WISP). Choose a new coordinator in",
                    "La FTC Safeguards Rule exige que cada oficina designe a alguien que supervise su plan de seguridad de la información (WISP). Elige un nuevo responsable en"
                ),
                ("Choose a coordinator", "Elegir responsable"),
                (
                    "You're receiving this because you're an administrator of your office on {{ product_name }}.",
                    "Recibes este correo porque eres administrador de tu oficina en {{ product_name }}."
                ),
            ]
        ),
        ["campaign.schedules_paused_on_offboarding.v1"] = new(
            "{% if campaign_count == 1 %}Se pausó una campaña programada{% else %}Se pausaron {{ campaign_count }} campañas programadas{% endif %}",
            "Se pausaron algunas campañas programadas. Cancélalas o vuelve a programarlas.",
            [
                (">Campaigns<", ">Campañas<"),
                (
                    "{% if campaign_count == 1 %}A scheduled campaign was paused{% else %}Scheduled campaigns were paused{% endif %}",
                    "{% if campaign_count == 1 %}Se pausó una campaña programada{% else %}Se pausaron campañas programadas{% endif %}"
                ),
                (
                    "{% if campaign_count == 1 %}This campaign was{% else %}These campaigns were{% endif %} scheduled by someone who's no longer with your office. So nothing goes out in their name, we paused {% if campaign_count == 1 %}it{% else %}them{% endif %}:",
                    "{% if campaign_count == 1 %}Esta campaña la programó{% else %}Estas campañas las programó{% endif %} alguien que ya no está en tu oficina. Para que no se envíe nada en su nombre, {% if campaign_count == 1 %}la pausamos{% else %}las pausamos{% endif %}:"
                ),
                (
                    "Open Campaigns to cancel {% if campaign_count == 1 %}it{% else %}them{% endif %} or schedule {% if campaign_count == 1 %}it{% else %}them{% endif %} again. Nothing will be sent until you do.",
                    "Abre Campaigns para {% if campaign_count == 1 %}cancelarla o volver a programarla{% else %}cancelarlas o volver a programarlas{% endif %}. No se enviará nada hasta que lo hagas."
                ),
                (">Open Campaigns<", ">Abrir Campaigns<"),
                (
                    "You're receiving this because you manage campaigns for your office on {{ product_name }}.",
                    "Recibes este correo porque administras las campañas de tu oficina en {{ product_name }}."
                ),
            ]
        ),
        // Asunto único en español: el bilingüe por motivo pasaba el tope de 500 caracteres; el cuerpo sí va por motivo.
        ["sig.preparer_attention_required.v1"] = new(
            "“{{ request_title }}” requiere tu atención",
            "Una solicitud de firma requiere tu atención porque su preparador ya no está en tu oficina.",
            [
                (">Signatures<", ">Firmas<"),
                (
                    "{% if reason == 'PreparerCleared' %}Choose a new preparer{% elsif reason == 'PreparerChangeBlocked' %}This request needs a new preparer{% elsif reason == 'UnsentRequestKept' %}Review an unsent request{% else %}A signature request needs your attention{% endif %}",
                    "{% if reason == 'PreparerCleared' %}Elige un nuevo preparador{% elsif reason == 'PreparerChangeBlocked' %}Esta solicitud necesita un nuevo preparador{% elsif reason == 'UnsentRequestKept' %}Revisa una solicitud sin enviar{% else %}Una solicitud de firma requiere tu atención{% endif %}"
                ),
                (
                    """The preparer of <strong style="color:#23384B;">{{ request_title }}</strong> is no longer with your office, so they were removed from it. Choose a new preparer before you send it.""",
                    """El preparador de <strong style="color:#23384B;">{{ request_title }}</strong> ya no está en tu oficina, así que se quitó de la solicitud. Elige un nuevo preparador antes de enviarla."""
                ),
                (
                    """The preparer of <strong style="color:#23384B;">{{ request_title }}</strong> is no longer with your office and hasn't signed yet. Change the preparer so the return can be signed.""",
                    """El preparador de <strong style="color:#23384B;">{{ request_title }}</strong> ya no está en tu oficina y todavía no firmó. Cambia el preparador para que la declaración se pueda firmar."""
                ),
                (
                    "</strong> was created by someone who's no longer with your office and hasn't been sent. Review it and decide whether to send it or cancel it.",
                    "</strong> la creó alguien que ya no está en tu oficina y no se ha enviado. Revísala y decide si la envías o la cancelas."
                ),
                (
                    """Someone who worked on <strong style="color:#23384B;">{{ request_title }}</strong> is no longer with your office. Open it to see what needs to change.""",
                    """Alguien que trabajó en <strong style="color:#23384B;">{{ request_title }}</strong> ya no está en tu oficina. Ábrela para ver qué hay que cambiar."""
                ),
                ("Open the request", "Abrir la solicitud"),
                (
                    "You're receiving this because you work with signature requests at your office on {{ product_name }}.",
                    "Recibes este correo porque trabajas con solicitudes de firma en tu oficina en {{ product_name }}."
                ),
            ]
        ),
        ["sig.preparer_changed.v1"] = new(
            "{% if is_new_preparer %}Ahora eres el preparador de “{{ request_title }}”{% else %}Nuevo preparador para “{{ request_title }}”{% endif %}",
            "Una solicitud de firma tiene un nuevo preparador.",
            [
                (">Signatures<", ">Firmas<"),
                (
                    "{% if is_new_preparer %}You're now the preparer{% else %}New preparer{% endif %}",
                    "{% if is_new_preparer %}Ahora eres el preparador{% else %}Nuevo preparador{% endif %}"
                ),
                (
                    """The previous preparer of <strong style="color:#23384B;">{{ request_title }}</strong> is no longer with your office. """,
                    """El preparador anterior de <strong style="color:#23384B;">{{ request_title }}</strong> ya no está en tu oficina. """
                ),
                (
                    "You'll sign {% if is_offboarding %}this return{% else %}",
                    "Firmarás {% if is_offboarding %}esta declaración{% else %}"
                ),
                (
                    "{% endif %} as its preparer. Open the request to review it.",
                    "{% endif %} como su preparador. Abre la solicitud para revisarla."
                ),
                (
                    "</strong> is now the preparer of {% if is_offboarding %}this signature request{% else %}",
                    "</strong> es ahora el preparador de {% if is_offboarding %}esta solicitud de firma{% else %}"
                ),
                (">Open the request<", ">Abrir la solicitud<"),
                (
                    "You're receiving this because you're the preparer or the author of this signature request on {{ product_name }}.",
                    "Recibes este correo porque eres el preparador o el autor de esta solicitud de firma en {{ product_name }}."
                ),
            ]
        ),
        ["storage.cloud_connection_review_required.v1"] = new(
            "Vuelve a conectar {{ provider_name }} en tu oficina",
            "La conexión de almacenamiento en la nube de tu oficina se debe volver a conectar con una cuenta de la oficina.",
            [
                (">Storage<", ">Almacenamiento<"),
                ("Reconnect your {{ provider_name }} connection", "Vuelve a conectar {{ provider_name }}"),
                (
                    """Your office's {{ provider_name }} connection uses the account <strong style="color:#23384B;">{{ account_email }}</strong>, which belongs to someone who's no longer with your office.""",
                    """La conexión de {{ provider_name }} de tu oficina usa la cuenta <strong style="color:#23384B;">{{ account_email }}</strong>, que pertenece a alguien que ya no está en tu oficina."""
                ),
                (
                    """Your office's {{ provider_name }} connection (<strong style="color:#23384B;">{{ account_email }}</strong>) was set up by someone who's no longer with your office.""",
                    """La conexión de {{ provider_name }} de tu oficina (<strong style="color:#23384B;">{{ account_email }}</strong>) la configuró alguien que ya no está en tu oficina."""
                ),
                (
                    """Your office's {{ provider_name }} connection (<strong style="color:#23384B;">{{ account_email }}</strong>) is linked to someone who's no longer with your office.""",
                    """La conexión de {{ provider_name }} de tu oficina (<strong style="color:#23384B;">{{ account_email }}</strong>) está ligada a alguien que ya no está en tu oficina."""
                ),
                (
                    "Reconnect it with an account that belongs to your office, or disconnect it.",
                    "Vuelve a conectarla con una cuenta que pertenezca a tu oficina, o desconéctala."
                ),
                ("Open storage settings", "Abrir la configuración de almacenamiento"),
                (
                    "You're receiving this because you manage your office's storage on {{ product_name }}.",
                    "Recibes este correo porque administras el almacenamiento de tu oficina en {{ product_name }}."
                ),
            ]
        ),
        ["connectors.office_mailbox_review_required.v1"] = new(
            "Vuelve a conectar el buzón de la oficina {{ mailbox_email }}",
            "Un buzón de la oficina se debe volver a conectar con una cuenta que controle tu oficina.",
            [
                (">Email<", ">Correo<"),
                ("Reconnect an office mailbox", "Vuelve a conectar un buzón de la oficina"),
                (
                    """The office mailbox <strong style="color:#23384B;">{{ mailbox_email }}</strong> is the address of someone who's no longer with your office.""",
                    """El buzón de la oficina <strong style="color:#23384B;">{{ mailbox_email }}</strong> es la dirección de alguien que ya no está en tu oficina."""
                ),
                (
                    """The office mailbox <strong style="color:#23384B;">{{ mailbox_email }}</strong> was connected by someone who's no longer with your office.""",
                    """El buzón de la oficina <strong style="color:#23384B;">{{ mailbox_email }}</strong> lo conectó alguien que ya no está en tu oficina."""
                ),
                (
                    """The office mailbox <strong style="color:#23384B;">{{ mailbox_email }}</strong> is linked to someone who's no longer with your office.""",
                    """El buzón de la oficina <strong style="color:#23384B;">{{ mailbox_email }}</strong> está ligado a alguien que ya no está en tu oficina."""
                ),
                (
                    "Reconnect it with an account your office controls, or disconnect it.",
                    "Vuelve a conectarlo con una cuenta que controle tu oficina, o desconéctalo."
                ),
                ("Open the mailbox", "Abrir el buzón"),
                (
                    "You're receiving this because you manage your office's mailboxes on {{ product_name }}.",
                    "Recibes este correo porque administras los buzones de tu oficina en {{ product_name }}."
                ),
            ]
        ),

        // ---------- Citas y reuniones ----------
        ["communication.meeting.invitation"] = new(
            "{% if in_progress %}Únete ahora: {{ meeting_title }}{% else %}Invitación: {{ meeting_title }}{% endif %}",
            "Te invitaron a una reunión: únete con el enlace de este correo.",
            [
                ("Meeting invitation", "Invitación a una reunión"),
                (
                    "{% if in_progress %}A meeting is in progress{% else %}You've been invited to a meeting{% endif %}",
                    "{% if in_progress %}Hay una reunión en curso{% else %}Te invitaron a una reunión{% endif %}"
                ),
                (
                    """Hi <strong style="color:#23384B;">{{ invitee_name }}</strong>, {% if in_progress %}you've been invited to join <strong style="color:#23384B;">{{ meeting_title }}</strong>, which is happening right now.{% else %}you've been invited to <strong style="color:#23384B;">{{ meeting_title }}</strong>.{% endif %}""",
                    """Hola{% if invitee_name != blank and invitee_name != 'there' %}, <strong style="color:#23384B;">{{ invitee_name }}</strong>{% endif %}. {% if in_progress %}Te invitaron a unirte a <strong style="color:#23384B;">{{ meeting_title }}</strong>, que está ocurriendo ahora mismo.{% else %}Te invitaron a <strong style="color:#23384B;">{{ meeting_title }}</strong>.{% endif %}"""
                ),
                .. MeetingDetailsEs,
                (
                    "{% if in_progress %}Use the button below to join now.{% else %}Use the button below to join when it's time.{% if start_local != blank %} A calendar invitation is attached so you can add it to your calendar.{% endif %}{% endif %}",
                    "{% if in_progress %}Usa el botón de abajo para unirte ahora.{% else %}Usa el botón de abajo para unirte cuando sea la hora.{% if start_local != blank %} Adjuntamos una invitación de calendario para que la agregues a tu calendario.{% endif %}{% endif %}"
                ),
                (
                    "{% if in_progress %}Join now{% else %}Join the meeting{% endif %}",
                    "{% if in_progress %}Unirme ahora{% else %}Unirme a la reunión{% endif %}"
                ),
                ButtonFallbackEs,
                .. RsvpEs,
                (
                    "This invitation expires {{ expires_at | office_deadline }}. If you weren't expecting it, you can ignore this email.",
                    "Esta invitación vence {{ expires_at | office_deadline }}. Si no la esperabas, puedes ignorar este correo."
                ),
            ]
        ),
        ["communication.meeting.cancelled.v1"] = new(
            "Cancelada: {{ meeting_title }}",
            "Se canceló una reunión a la que te invitaron.",
            [
                (">Meeting<", ">Reunión<"),
                ("The meeting was cancelled", "La reunión se canceló"),
                (
                    "{% if start_local != blank %}, scheduled for {{ start_local }},{% endif %} was cancelled{% if host_name != blank %} by {{ host_name }}{% endif %}.",
                    "{% if start_local != blank %}, programada para el {{ start_local }},{% endif %} fue cancelada{% if host_name != blank %} por {{ host_name }}{% endif %}."
                ),
                (
                    "No action is needed.{% if start_local != blank %} The attached calendar update removes it from your calendar.{% endif %}",
                    "No tienes que hacer nada.{% if start_local != blank %} La actualización de calendario adjunta la quita de tu calendario.{% endif %}"
                ),
            ]
        ),
        ["communication.meeting.rescheduled.v1"] = new(
            "Reprogramada: {{ meeting_title }}",
            "Una reunión a la que te invitaron tiene un nuevo horario.",
            [
                (">Meeting<", ">Reunión<"),
                ("The meeting was rescheduled", "La reunión se reprogramó"),
                (
                    "</strong> has a new time{% if host_name != blank %}, set by {{ host_name }}{% endif %}.",
                    "</strong> tiene un nuevo horario{% if host_name != blank %}, definido por {{ host_name }}{% endif %}."
                ),
                ("<strong>Previously:</strong>", "<strong>Antes:</strong>"),
                ("New time:</strong>", "Nuevo horario:</strong>"),
                ("The new time has not been set yet.", "Todavía no se definió el nuevo horario."),
                ("Meeting code:</strong>", "Código de la reunión:</strong>"),
                (
                    "Your invitation link still works.{% if new_local != blank %} The attached calendar update moves it in your calendar.{% endif %}",
                    "Tu enlace de invitación sigue funcionando.{% if new_local != blank %} La actualización de calendario adjunta la mueve en tu calendario.{% endif %}"
                ),
            ]
        ),
        ["communication.meeting.reminder.v1"] = new(
            "Empieza en {{ minutes_before }} min: {{ meeting_title }}",
            "Tu reunión está por empezar.",
            [
                ("Meeting reminder", "Recordatorio de reunión"),
                (
                    "Your meeting starts in {{ minutes_before }} minutes",
                    "Tu reunión empieza en {{ minutes_before }} minutos"
                ),
                ("</strong> is about to start.", "</strong> está por empezar."),
                .. MeetingDetailsEs,
                (
                    "Use the link from your invitation email to join{% if meeting_code != blank %}, or enter the meeting code{% endif %}.",
                    "Usa el enlace del correo de invitación para unirte{% if meeting_code != blank %}, o escribe el código de la reunión{% endif %}."
                ),
            ]
        ),
        ["calendar.appointment_attendee_added.v1"] = new(
            "{{ product_name }} — Te agregaron a {{ appointment_title }}",
            "Te agregaron a una cita. Aquí están los detalles.",
            [
                (">Appointment<", ">Cita<"),
                ("You're on the guest list", "Estás en la lista de invitados"),
                (
                    """The office added you to <strong style="color:#1E466B;">{{ appointment_title }}</strong>.""",
                    """La oficina te agregó a <strong style="color:#1E466B;">{{ appointment_title }}</strong>."""
                ),
                (
                    "It repeats: check the calendar for all dates.",
                    "Se repite: revisa el calendario para ver todas las fechas."
                ),
                (
                    "It's a video meeting; the link is in the appointment.",
                    "Es una videollamada; el enlace está en la cita."
                ),
                (
                    "Someone at the office added you to this appointment after it was booked.",
                    "Alguien de la oficina te agregó a esta cita después de agendarla."
                ),
                .. RsvpEs,
            ]
        ),
        ["calendar.appointment_scheduled.v1"] = new(
            null,
            "Tu cita quedó agendada. Aquí están los detalles.",
            [
                (">Appointment<", ">Cita<"),
                ("You have a new appointment", "Tienes una cita nueva"),
                ("You've been scheduled for: <strong", "Te agendaron para: <strong"),
                (
                    "It repeats: check the calendar for all dates.",
                    "Se repite: revisa el calendario para ver todas las fechas."
                ),
                (
                    "It's a video meeting; the link is in the appointment.",
                    "Es una videollamada; el enlace está en la cita."
                ),
                .. RsvpEs,
            ]
        ),
        ["calendar.appointment_rescheduled.v1"] = new(
            "{{ product_name }} — Tu cita cambió de horario",
            "La hora de tu cita cambió. Mira los nuevos detalles.",
            [
                (">Appointment<", ">Cita<"),
                ("Your appointment moved", "Tu cita cambió de horario"),
                ("Before: <s>", "Antes: <s>"),
                ("Now: <strong>", "Ahora: <strong>"),
                .. RsvpEs,
            ]
        ),
        ["calendar.appointment_updated.v1"] = new(
            "{{ product_name }} — Actualizada: {{ appointment_title }}",
            "Cambiaron los detalles de tu cita. Esto es lo nuevo.",
            [
                (">Appointment<", ">Cita<"),
                ("Your appointment was updated", "Tu cita se actualizó"),
                ("Before: <s>", "Antes: <s>"),
                ("New place: ", "Nuevo lugar: "),
                ("Notes:</strong>", "Notas:</strong>"),
                (
                    "The time didn't change, and your answer still stands.",
                    "La hora no cambió y tu respuesta sigue en pie."
                ),
            ]
        ),
        ["calendar.booking_declined.v1"] = new(
            "{{ product_name }} — No pudimos confirmar {{ appointment_title }}",
            "Tu oficina no pudo confirmar la hora que pediste.",
            [
                ("Appointment request", "Solicitud de cita"),
                ("We couldn't confirm your appointment", "No pudimos confirmar tu cita"),
                ("From your office:</strong>", "Mensaje de tu oficina:</strong>"),
                ("You can pick another time from your portal: <a", "Puedes elegir otra hora desde tu portal: <a"),
                ("Book another time</a>", "Reservar otra hora</a>"),
            ]
        ),
        ["calendar.appointment_cancelled.v1"] = new(
            "{{ product_name }} — Tu cita se canceló",
            "Tu cita se canceló. Esto es lo que pasó.",
            [
                (">Appointment<", ">Cita<"),
                (">Your appointment was cancelled</td>", ">Tu cita se canceló</td>"),
                (
                    """Your appointment on <strong style="color:#23384B;">{{ start_local }}</strong> was cancelled.""",
                    """Tu cita del <strong style="color:#23384B;">{{ start_local }}</strong> se canceló."""
                ),
                ("An appointment you had scheduled was cancelled.", "Se canceló una cita que tenías agendada."),
                ("No action is needed.", "No tienes que hacer nada."),
            ]
        ),

        // ---------- Cuenta ----------
        // Sin la variante de reenvío en español: con ella el asunto bilingüe pasaba el tope de 500 caracteres.
        ["auth.invitation"] = new(
            "{% if account_kind == 'portal' %}Activa tu portal en {{ office }}{% else %}Te invitaron a {{ office }} en {{ product_name }}{% endif %}",
            "Te invitaron a colaborar: activa tu cuenta para empezar.",
            [
                (">Invitation<", ">Invitación<"),
                (
                    "{% if account_kind == 'portal' %}Your client portal is ready{% else %}You've been invited to collaborate{% endif %}",
                    "{% if account_kind == 'portal' %}Tu portal de cliente está listo{% else %}Te invitaron a colaborar{% endif %}"
                ),
                (
                    "</strong> invited you to your client portal on {{ product_name }}.",
                    "</strong> te invitó a tu portal de cliente en {{ product_name }}."
                ),
                (
                    """</strong> invited you to join <strong style="color:#1E466B;">{{ office }}</strong> on {{ product_name }}.""",
                    """</strong> te invitó a unirte a <strong style="color:#1E466B;">{{ office }}</strong> en {{ product_name }}."""
                ),
                (
                    "Activate your account to see your documents, sign what your office sends you and message them securely.",
                    "Activa tu cuenta para ver tus documentos, firmar lo que te envíe tu oficina y escribirle de forma segura."
                ),
                (
                    "Activate your account to access the workspace and start collaborating.",
                    "Activa tu cuenta para entrar al espacio de trabajo y empezar a colaborar."
                ),
                ("Activate my account", "Activar mi cuenta"),
                ButtonFallbackEs,
                (
                    "This link expires {{ expires_at | office_deadline }}. If you weren't expecting this invitation, you can ignore this email.",
                    "Este enlace vence {{ expires_at | office_deadline }}. Si no esperabas esta invitación, puedes ignorar este correo."
                ),
            ]
        ),
        ["auth.password_reset"] = new(
            "Restablece tu contraseña {% if account_kind == 'portal' %}del portal de cliente{% else %}de {{ product_name }}{% endif %}",
            "Usa el enlace seguro de este correo para restablecer tu contraseña. Vence pronto.",
            [
                (">Security<", ">Seguridad<"),
                ("Reset your password", "Restablece tu contraseña"),
                (
                    """We received a request to reset the password for your {% if account_kind == 'portal' %}client portal account at <strong style="color:#23384B;">{{ office }}</strong>{% else %}<strong style="color:#23384B;">{{ office }}</strong> workspace account{% endif %}. Only this account changes: any other {{ product_name }} account you have keeps its password.""",
                    """Recibimos una solicitud para restablecer la contraseña de tu {% if account_kind == 'portal' %}cuenta del portal de cliente en <strong style="color:#23384B;">"""
                        + OfficeEs
                        + """</strong>{% else %}cuenta del espacio de trabajo de <strong style="color:#23384B;">"""
                        + OfficeEs
                        + """</strong>{% endif %}. Solo cambia esta cuenta: cualquier otra cuenta de {{ product_name }} que tengas conserva su contraseña."""
                ),
                ("Reset password", "Restablecer contraseña"),
                ButtonFallbackEs,
                (
                    "This link expires {{ expires_at | office_deadline }}. If you didn't request this, ignore this email: your current password stays valid.",
                    "Este enlace vence {{ expires_at | office_deadline }}. Si no lo pediste, ignora este correo: tu contraseña actual sigue siendo válida."
                ),
            ]
        ),
        ["auth.password-reset.office"] = new(
            "{{ office }} te envió un enlace para restablecer tu contraseña{% if account_kind == 'portal' %} del portal de cliente{% endif %}",
            "Crea una contraseña nueva para tu cuenta {% if account_kind == 'portal' %}del portal de cliente{% else %}de equipo{% endif %}. El enlace solo se puede usar una vez.",
            [
                (">Security<", ">Seguridad<"),
                ("Set a new password", "Crea una contraseña nueva"),
                (
                    """An office administrator at <strong style="color:#23384B;">{{ office }}</strong> sent you a link to set a new password for your {% if account_kind == 'portal' %}client portal account{% else %}{{ product_name }} team account{% endif %}.""",
                    """Un administrador de la oficina <strong style="color:#23384B;">"""
                        + OfficeEs
                        + """</strong> te envió un enlace para crear una contraseña nueva para tu {% if account_kind == 'portal' %}cuenta del portal de cliente{% else %}cuenta de equipo de {{ product_name }}{% endif %}."""
                ),
                ButtonFallbackEs,
                (
                    "This link expires {{ expires_at | office_deadline }} and can be used once. Your current password keeps working until you set a new one. After you change it, you'll be signed out of all devices.",
                    "Este enlace vence {{ expires_at | office_deadline }} y solo se puede usar una vez. Tu contraseña actual sigue funcionando hasta que crees una nueva. Después de cambiarla, se cerrará tu sesión en todos los dispositivos."
                ),
                (
                    "{% if account_kind == 'portal' %}Your documents and information aren't changed. {% endif %}If you use two-step verification, you'll still need your code to sign in.",
                    "{% if account_kind == 'portal' %}Tus documentos y tu información no cambian. {% endif %}Si usas la verificación en dos pasos, seguirás necesitando tu código para iniciar sesión."
                ),
                (
                    """<strong style="color:#23384B;">Didn't expect this?</strong> You don't need to do anything: your password won't change unless you use the link. If you think something is wrong, contact {{ office }} directly using the phone number or email you usually use with them.""",
                    """<strong style="color:#23384B;">¿No lo esperabas?</strong> No tienes que hacer nada: tu contraseña no cambiará a menos que uses el enlace. Si crees que algo está mal, comunícate directamente con {{ office }} por el teléfono o el correo que sueles usar con ellos."""
                ),
                (
                    "This email is about your account at {{ office }} on {{ product_name }}. It doesn't affect any other office you may use. {{ product_name }} and {{ office }} will never ask you for your password by email or phone.",
                    "Este correo trata sobre tu cuenta en {{ office }} en {{ product_name }}. No afecta a ninguna otra oficina que uses. {{ product_name }} y {{ office }} nunca te pedirán tu contraseña por correo ni por teléfono."
                ),
            ]
        ),
        // El motivo llega como código (reason_code) y aquí se escribe en español; sin código vale reason.
        ["auth.account-secured"] = new(
            "Acción necesaria: se aseguró tu cuenta de " + OfficeEs,
            "Crea una contraseña nueva para volver a entrar. El enlace solo se puede usar una vez.",
            [
                (">Security<", ">Seguridad<"),
                ("Your account was secured", "Se aseguró tu cuenta"),
                (
                    """An office administrator at <strong style="color:#23384B;">{{ office }}</strong> secured your {% if account_kind == 'portal' %}client portal account{% else %}{{ product_name }} team account{% endif %} as a precaution. This usually happens when there's a concern that someone else could access it.""",
                    """Un administrador de la oficina <strong style="color:#23384B;">"""
                        + OfficeEs
                        + """</strong> aseguró tu {% if account_kind == 'portal' %}cuenta del portal de cliente{% else %}cuenta de equipo de {{ product_name }}{% endif %} como precaución. Esto suele pasar cuando preocupa que otra persona pueda entrar a ella."""
                ),
                (
                    """<strong style="color:#23384B;">What changed:</strong>""",
                    """<strong style="color:#23384B;">Qué cambió:</strong>"""
                ),
                ("&bull; Your previous password no longer works.", "&bull; Ya no funciona tu contraseña anterior."),
                (
                    "&bull; You've been signed out of all devices.",
                    "&bull; Se cerró tu sesión en todos los dispositivos."
                ),
                ("To get back in, set a new password:", "Para volver a entrar, crea una contraseña nueva:"),
                ("Set a new password", "Crea una contraseña nueva"),
                ButtonFallbackEs,
                (
                    """This link expires {{ expires_at | office_deadline }} and can be used once. If it expires, use "Forgot password?" on the {{ office }} sign-in page.""",
                    """Este enlace vence {{ expires_at | office_deadline }} y solo se puede usar una vez. Si vence, usa "¿Olvidaste tu contraseña?" en la página de inicio de sesión de """
                        + OfficeEs
                        + "."
                ),
                (
                    "Choose a password you don't use anywhere else. If you used the same password on other sites or with other offices, change it there too.",
                    "Elige una contraseña que no uses en ningún otro lugar. Si usaste la misma contraseña en otros sitios o con otras oficinas, cámbiala allí también."
                ),
                (
                    """<strong style="color:#23384B;">Didn't expect this, or have questions?</strong> Contact {{ office }} directly using the phone number or email you usually use with them.""",
                    """<strong style="color:#23384B;">¿No lo esperabas o tienes preguntas?</strong> Comunícate directamente con """
                        + OfficeEs
                        + " por el teléfono o el correo que sueles usar."
                ),
                (
                    "This email is about your account at {{ office }} on {{ product_name }}. It doesn't affect any other office you may use. {{ product_name }} and {{ office }} will never ask you for your password by email or phone.",
                    "Este correo trata sobre tu cuenta en "
                        + OfficeEs
                        + " en {{ product_name }}. No afecta a ninguna otra oficina que uses. {{ product_name }} y "
                        + OfficeEs
                        + " nunca te pedirán tu contraseña por correo ni por teléfono."
                ),
            ]
        ),
        ["auth.password-change-required"] = new(
            "{% if tenant_name %}{{ tenant_name }}{% else %}Tu oficina{% endif %} te pide actualizar tu contraseña",
            "Elegirás una contraseña nueva la próxima vez que inicies sesión.",
            [
                ("""{% assign office = "your office" %}""", """{% assign office = "tu oficina" %}"""),
                (">Security<", ">Seguridad<"),
                ("Update your password", "Actualiza tu contraseña"),
                (
                    """An office administrator at <strong style="color:#23384B;">{{ office }}</strong> asked you to choose a new password for your {% if account_kind == 'portal' %}client portal account{% else %}{{ product_name }} team account{% endif %}.""",
                    """Un administrador de la oficina <strong style="color:#23384B;">{{ office }}</strong> te pidió elegir una contraseña nueva para tu {% if account_kind == 'portal' %}cuenta del portal de cliente{% else %}cuenta de equipo de {{ product_name }}{% endif %}."""
                ),
                (
                    "For your security, you've been signed out of all devices. Next time you sign in with your current password, we'll ask you to set a new one before you continue.",
                    "Por tu seguridad, se cerró tu sesión en todos los dispositivos. La próxima vez que inicies sesión con tu contraseña actual, te pediremos crear una nueva antes de continuar."
                ),
                ("Sign in to {{ office }}", "Iniciar sesión en {{ office }}"),
                (
                    """Forgot your current password? Use "Forgot password?" on the sign-in page.""",
                    """¿Olvidaste tu contraseña actual? Usa "¿Olvidaste tu contraseña?" en la página de inicio de sesión."""
                ),
                (
                    """<strong style="color:#23384B;">Didn't expect this?</strong> Contact {{ office }} directly using the phone number or email you usually use with them.""",
                    """<strong style="color:#23384B;">¿No lo esperabas?</strong> Comunícate directamente con {{ office }} por el teléfono o el correo que sueles usar."""
                ),
                (
                    "This email is about your account at {{ office }} on {{ product_name }}. It doesn't affect any other office you may use. {{ product_name }} and {{ office }} will never ask you for your password by email or phone.",
                    "Este correo trata sobre tu cuenta en {{ office }} en {{ product_name }}. No afecta a ninguna otra oficina que uses. {{ product_name }} y {{ office }} nunca te pedirán tu contraseña por correo ni por teléfono."
                ),
            ]
        ),
        ["auth.password-changed"] = new(
            "Se cambió tu contraseña{% if tenant_name %} de {{ tenant_name }}{% endif %}",
            "Si fuiste tú, no tienes que hacer nada más.",
            [
                ("""{% assign office = "your office" %}""", """{% assign office = "tu oficina" %}"""),
                (">Security<", ">Seguridad<"),
                ("Your password was changed", "Se cambió tu contraseña"),
                (
                    """The password for your {% if account_kind == 'portal' %}client portal account{% else %}{{ product_name }} team account{% endif %} at <strong style="color:#23384B;">{{ office }}</strong> was changed{% if changed_at %} on {{ changed_at | office_date }}{% endif %}.""",
                    """Se cambió la contraseña de tu {% if account_kind == 'portal' %}cuenta del portal de cliente{% else %}cuenta de equipo de {{ product_name }}{% endif %} en <strong style="color:#23384B;">{{ office }}</strong>{% if changed_at %} el {{ changed_at | office_date }}{% endif %}."""
                ),
                (
                    "You've been signed out of your other devices. Sign in again with your new password.",
                    "Se cerró tu sesión en tus otros dispositivos. Vuelve a iniciar sesión con tu contraseña nueva."
                ),
                (
                    """<strong style="color:#23384B;">Wasn't you?</strong> Use "Forgot password?" on the {{ office }} sign-in page right away to set a new password, then contact {{ office }} directly using the phone number or email you usually use with them.""",
                    """<strong style="color:#23384B;">¿No fuiste tú?</strong> Usa de inmediato "¿Olvidaste tu contraseña?" en la página de inicio de sesión de {{ office }} para crear una contraseña nueva y luego comunícate directamente con {{ office }} por el teléfono o el correo que sueles usar."""
                ),
                ("Go to {{ office }} sign-in", "Ir al inicio de sesión de {{ office }}"),
                (
                    "This email is about your account at {{ office }} on {{ product_name }}. It doesn't affect any other office you may use. {{ product_name }} and {{ office }} will never ask you for your password by email or phone.",
                    "Este correo trata sobre tu cuenta en {{ office }} en {{ product_name }}. No afecta a ninguna otra oficina que uses. {{ product_name }} y {{ office }} nunca te pedirán tu contraseña por correo ni por teléfono."
                ),
            ]
        ),
        ["auth.otp_code"] = new(
            "{{ code }} es tu código de {{ product_name }}",
            "Aquí tienes tu código de verificación de un solo uso. Vale por unos minutos.",
            [
                (">Verification<", ">Verificación<"),
                ("Your verification code</td>", "Tu código de verificación</td>"),
                (
                    """Your verification code for <strong style="color:#23384B;">""",
                    """Tu código de verificación para <strong style="color:#23384B;">"""
                ),
                (
                    "{% when 'login' %}signing in{% when 'email_change' %}confirming your new email{% when 'phone_verify', 'phone_verification' %}verifying your phone{% when 'continue' %}continuing{% else %}",
                    "{% when 'login' %}iniciar sesión{% when 'email_change' %}confirmar tu nuevo correo{% when 'phone_verify', 'phone_verification' %}verificar tu teléfono{% when 'continue' %}continuar{% else %}"
                ),
                ("</strong> is:", "</strong> es:"),
                ("Valid for a few minutes", "Válido por unos minutos"),
                (
                    "Never share this code. The {{ product_name }} team will never ask you for it by phone, chat, or email.",
                    "Nunca compartas este código. El equipo de {{ product_name }} nunca te lo pedirá por teléfono, chat ni correo."
                ),
            ]
        ),
        ["auth.email_change"] = new(
            "Confirma tu nuevo correo en {{ product_name }}",
            "Confirma tu nueva dirección de correo para mantener tu cuenta segura.",
            [
                (">Security<", ">Seguridad<"),
                ("Confirm your new email</td>", "Confirma tu nuevo correo</td>"),
                (
                    """You requested to change the email of your {% if account_kind == 'portal' %}client portal account at <strong style="color:#23384B;">{{ office }}</strong>{% else %}<strong style="color:#23384B;">{{ office }}</strong> workspace account{% endif %}. Confirm the new address to activate it:""",
                    """Pediste cambiar el correo de tu {% if account_kind == 'portal' %}cuenta del portal de cliente en <strong style="color:#23384B;">"""
                        + OfficeEs
                        + """</strong>{% else %}cuenta del espacio de trabajo de <strong style="color:#23384B;">"""
                        + OfficeEs
                        + """</strong>{% endif %}. Confirma la nueva dirección para activarla:"""
                ),
                ("Confirm new email", "Confirmar nuevo correo"),
                (
                    "This link expires {{ expires_at | office_deadline }}. If you didn't request this change, contact your office administrator.",
                    "Este enlace vence {{ expires_at | office_deadline }}. Si no pediste este cambio, contacta al administrador de tu oficina."
                ),
            ]
        ),
        ["auth.security_alert"] = new(
            "Alerta de seguridad en tu cuenta de {{ product_name }}",
            "Detectamos actividad importante en tu cuenta. Revísala.",
            [
                (">Security<", ">Seguridad<"),
                (">Security alert<", ">Alerta de seguridad<"),
                ("<br />IP address: <strong>", "<br />Dirección IP: <strong>"),
                (
                    "If you recognize this activity you can ignore this email. If it wasn't you, change your password immediately and contact your administrator.",
                    "Si reconoces esta actividad, puedes ignorar este correo. Si no fuiste tú, cambia tu contraseña de inmediato y contacta a tu administrador."
                ),
            ]
        ),
        ["auth.tenant_recovery"] = new(
            "Tus oficinas en {{ product_name }}",
            "Estas son las oficinas vinculadas a tu correo para que puedas volver a entrar.",
            [
                (">Account<", ">Cuenta<"),
                (">Your offices<", ">Tus oficinas<"),
                (
                    "We found the following offices linked to your email:",
                    "Encontramos estas oficinas vinculadas a tu correo:"
                ),
                (
                    "If you don't recognize this request, you can ignore this email.",
                    "Si no reconoces esta solicitud, puedes ignorar este correo."
                ),
            ]
        ),
        ["auth.welcome"] = new(
            "¡Te damos la bienvenida a {{ product_name }}!",
            "Tu cuenta está lista: así puedes sacarle el máximo provecho.",
            [
                (">Welcome<", ">Bienvenida<"),
                ("Welcome aboard!", "¡Te damos la bienvenida!"),
                (
                    """Hi <strong style="color:#23384B;">{{ name }}</strong>, your account is ready. Sign in and get started.""",
                    """Hola, <strong style="color:#23384B;">{{ name }}</strong>. Tu cuenta está lista. Inicia sesión y empieza."""
                ),
                ("Go to {{ product_name }}", "Ir a {{ product_name }}"),
            ]
        ),

        // ---------- Dueño y cobros ----------
        ["seats.user_gated.v1"] = new(
            "Un miembro del equipo perdió el acceso a {{ tenantName }}",
            "Un miembro del equipo perdió el acceso porque la oficina superó su límite de puestos activos.",
            [
                ("Team access", "Acceso del equipo"),
                (">A team member lost access</td>", ">Un miembro del equipo perdió el acceso</td>"),
                (
                    "</strong> ({{ userEmail }}) can no longer access the office because seat #{{ slotIndex }} is suspended. Pay the add-on seat or deactivate another user to free a slot.",
                    "</strong> ({{ userEmail }}) ya no puede entrar a la oficina porque el puesto n.º {{ slotIndex }} está suspendido. Paga el puesto adicional o desactiva a otro usuario para liberar un lugar."
                ),
                ("Open billing", "Abrir facturación"),
            ]
        ),
        ["seats.user_unlocked.v1"] = new(
            "Se restableció el acceso de {{ userFullName }}",
            "Un miembro del equipo puede volver a entrar a la oficina.",
            [
                ("Team access", "Acceso del equipo"),
                ("Access restored", "Acceso restablecido"),
                (
                    "</strong> can access {{ tenantName }} again after the seat was reactivated.",
                    "</strong> puede volver a entrar a {{ tenantName }} porque se reactivó su puesto."
                ),
            ]
        ),
        ["referral.reward.granted"] = new(
            "Tu premio de referido de {{ amount_label }} ya está en tu Wallet",
            "Tu premio de referido se agregó a tu Wallet: úsalo en tu próximo pago.",
            [
                (">Referrals<", ">Referidos<"),
                ("Your referral reward is in your Wallet", "Tu premio de referido ya está en tu Wallet"),
                (
                    """An office you referred paid its plan, and the waiting period is over. We added <strong style="color:#23384B;">{{ amount_label }}</strong> to your office's Wallet.""",
                    """Una oficina que referiste pagó su plan y ya terminó el periodo de espera. Agregamos <strong style="color:#23384B;">{{ amount_label }}</strong> al Wallet de tu oficina."""
                ),
                (
                    "Your Wallet balance is used on your next {{ product_name }} payment.",
                    "El saldo de tu Wallet se usa en tu próximo pago de {{ product_name }}."
                ),
                ("Open your Wallet", "Abrir tu Wallet"),
                (
                    "You're receiving this because you own your office on {{ product_name }} and someone signed up with your referral code.",
                    "Recibes este correo porque eres dueño de tu oficina en {{ product_name }} y alguien se registró con tu código de referido."
                ),
            ]
        ),
        ["onboarding.otp_code"] = new(
            "{{ otp_code }} es tu código de verificación de {{ product_name }}",
            "Aquí tienes tu código de verificación para seguir creando tu cuenta.",
            [
                (">Verification<", ">Verificación<"),
                ("Your verification code</td>", "Tu código de verificación</td>"),
                (
                    """Hi <strong style="color:#23384B;">{{ first_name }}</strong>, your code to continue signing up for {{ product_name }} is:""",
                    """Hola, <strong style="color:#23384B;">{{ first_name }}</strong>. Tu código para seguir con tu registro en {{ product_name }} es:"""
                ),
                (
                    "Expires in {{ expires_in_minutes }} minutes. Never share it: the {{ product_name }} team will never ask you for it.",
                    "Vence en {{ expires_in_minutes }} minutos. No lo compartas: el equipo de {{ product_name }} nunca te lo pedirá."
                ),
            ]
        ),
        ["onboarding.registration_ready"] = new(
            "Tu pago está confirmado: completa tu cuenta de {{ product_name }}",
            "Pago confirmado: termina de crear tu cuenta para empezar.",
            [
                (">Welcome<", ">Bienvenida<"),
                (
                    """Hi <strong style="color:#23384B;">{{ first_name }}</strong>, we confirmed your payment of <strong style="color:#1E466B;">{{ price_formatted }}</strong> on {{ paid_at | office_date }} for the {{ plan_name }} plan. You can now complete your account:""",
                    """Hola, <strong style="color:#23384B;">{{ first_name }}</strong>. Confirmamos tu pago de <strong style="color:#1E466B;">{{ price_formatted }}</strong> del {{ paid_at | office_date }} por el plan {{ plan_name }}. Ya puedes completar tu cuenta:"""
                ),
                ("Complete your account", "Completa tu cuenta"),
                (
                    "List price {{ gross_formatted }} &middot; Discount &minus;",
                    "Precio de lista {{ gross_formatted }} &middot; Descuento &minus;"
                ),
                ("Complete my account", "Completar mi cuenta"),
                ("Download receipt", "Descargar recibo"),
                (
                    "If you don't recognize this purchase, contact support immediately.",
                    "Si no reconoces esta compra, contacta a soporte de inmediato."
                ),
            ]
        ),
        ["onboarding.receipt_ready"] = new(
            "Tu recibo de pago de {{ product_name }} está listo",
            "Tu recibo de pago está listo para ver y descargar.",
            [
                (">Receipt<", ">Recibo<"),
                ("Your receipt is ready", "Tu recibo está listo"),
                (
                    """Hi <strong style="color:#23384B;">{{ first_name }}</strong>, your {{ product_name }} payment receipt is ready to download:""",
                    """Hola, <strong style="color:#23384B;">{{ first_name }}</strong>. Tu recibo de pago de {{ product_name }} está listo para descargar:"""
                ),
                ("Download receipt", "Descargar recibo"),
            ]
        ),
        ["onboarding.payment_failed"] = new(
            "Hubo un problema con tu pago de {{ product_name }}",
            "Tu pago no se completó: puedes intentarlo de nuevo en un momento.",
            [
                (">Payment<", ">Pago<"),
                ("Your payment didn't go through", "Tu pago no se completó"),
                (
                    """Hi <strong style="color:#23384B;">{{ first_name }}</strong>, we couldn't process your payment for the {{ plan_name }} plan, so your account wasn't created. No charge was made. You can try again below:""",
                    """Hola, <strong style="color:#23384B;">{{ first_name }}</strong>. No pudimos procesar tu pago del plan {{ plan_name }}, así que tu cuenta no se creó. No se hizo ningún cobro. Puedes intentarlo de nuevo aquí abajo:"""
                ),
                ("Reason: {{ failure_reason }}", "Motivo: {{ failure_reason }}"),
                ("Try again", "Intentar de nuevo"),
                (
                    "If you keep having trouble, reach out to support and we'll help you finish signing up.",
                    "Si sigues teniendo problemas, escribe a soporte y te ayudaremos a terminar tu registro."
                ),
            ]
        ),
        ["subscription.discount_ending"] = new(
            "Tu descuento de {{ discount_label }} termina en {{ days_left }} {% if days_left == '1' %}día{% else %}días{% endif %}",
            "Tu descuento termina pronto: después, tu plan se renueva a su precio normal.",
            [
                (">Billing<", ">Facturación<"),
                ("Your discount ends soon", "Tu descuento termina pronto"),
                (
                    """Your <strong style="color:#23384B;">{{ discount_label }}</strong> discount on {{ product_name }} ends in {{ days_left }} {% if days_left == '1' %}day{% else %}days{% endif %}.""",
                    """Tu descuento de <strong style="color:#23384B;">{{ discount_label }}</strong> en {{ product_name }} termina en {{ days_left }} {% if days_left == '1' %}día{% else %}días{% endif %}."""
                ),
                (
                    "After that, your plan renews at its regular price. There's nothing you need to do.",
                    "Después, tu plan se renueva a su precio normal. No tienes que hacer nada."
                ),
                ("Open {{ product_name }}", "Abrir {{ product_name }}"),
                (
                    "You're receiving this because you own your office on {{ product_name }}.",
                    "Recibes este correo porque eres dueño de tu oficina en {{ product_name }}."
                ),
            ]
        ),
        ["subscription.payment_failed"] = new(
            "Acción requerida: falló el pago de tu suscripción a {{ product_name }}",
            "No pudimos renovar tu suscripción: actualiza tu pago para mantener el acceso.",
            [
                (">Billing<", ">Facturación<"),
                ("We couldn't renew your subscription", "No pudimos renovar tu suscripción"),
                (
                    """Hi <strong style="color:#23384B;">{{ first_name }}</strong>, we couldn't process the renewal for your <strong style="color:#1E466B;">{{ plan_name }}</strong> plan.""",
                    """Hola, <strong style="color:#23384B;">{{ first_name }}</strong>. No pudimos procesar la renovación de tu plan <strong style="color:#1E466B;">{{ plan_name }}</strong>."""
                ),
                (
                    """ Your access stays active until <strong style="color:#23384B;">{{ grace_end_date }}</strong>, but it will be paused if the payment isn't updated by then.""",
                    """ Tu acceso sigue activo hasta el <strong style="color:#23384B;">{{ grace_end_date }}</strong>, pero se pausará si para entonces no actualizas el pago."""
                ),
                (
                    " Please update your payment method to keep your access.",
                    " Actualiza tu método de pago para mantener tu acceso."
                ),
                ("Reason: {{ failure_reason }}", "Motivo: {{ failure_reason }}"),
                ("Update payment", "Actualizar pago"),
                ButtonFallbackEs,
                (
                    "If you've already updated your payment method, you can ignore this email.",
                    "Si ya actualizaste tu método de pago, puedes ignorar este correo."
                ),
            ]
        ),
        ["subscription.suspended"] = new(
            "Tu suscripción a {{ product_name }} está en pausa",
            "Tu suscripción está en pausa. Renuévala para devolverle el acceso a tu equipo.",
            [
                (">Billing<", ">Facturación<"),
                ("Your subscription is on hold", "Tu suscripción está en pausa"),
                (
                    """Hi <strong style="color:#23384B;">{{ first_name }}</strong>, your <strong style="color:#1E466B;">{{ plan_name }}</strong> plan has been suspended because we couldn't collect payment. Your team's access is paused until the subscription is renewed — your data is safe in the meantime.""",
                    """Hola, <strong style="color:#23384B;">{{ first_name }}</strong>. Tu plan <strong style="color:#1E466B;">{{ plan_name }}</strong> se suspendió porque no pudimos cobrar el pago. El acceso de tu equipo queda en pausa hasta que renueves la suscripción; mientras tanto, tus datos están a salvo."""
                ),
                ("Renew subscription", "Renovar suscripción"),
                ButtonFallbackEs,
                (
                    "Need a hand? Reach out to support and we'll help you get back up and running.",
                    "¿Necesitas ayuda? Escribe a soporte y te ayudaremos a volver a ponerte en marcha."
                ),
            ]
        ),
        ["subscription.access_ending"] = new(
            "Tu acceso a {{ product_name }} termina el {{ access_ends_date | office_date }}",
            null,
            [
                (">Billing<", ">Facturación<"),
                ("Your access ends soon", "Tu acceso termina pronto"),
                (
                    """Hi <strong style="color:#23384B;">{{ first_name }}</strong>, you cancelled your <strong style="color:#1E466B;">{{ plan_name }}</strong> plan and it ends on <strong style="color:#1E466B;">{{ access_ends_date | office_date }}</strong>. That's in {{ days_until_end }} day(s). If you'd like to stay, you can undo the cancellation before then and nothing will have changed.""",
                    """Hola, <strong style="color:#23384B;">{{ first_name }}</strong>. Cancelaste tu plan <strong style="color:#1E466B;">{{ plan_name }}</strong> y termina el <strong style="color:#1E466B;">{{ access_ends_date | office_date }}</strong>, es decir, en {{ days_until_end }} día(s). Si quieres quedarte, puedes deshacer la cancelación antes de esa fecha y nada habrá cambiado."""
                ),
                ("Keep my subscription", "Mantener mi suscripción"),
                ButtonFallbackEs,
                (
                    "If you'd rather go ahead, no action is needed. Your data stays safe and you can come back whenever you want.",
                    "Si prefieres seguir adelante, no tienes que hacer nada. Tus datos quedan a salvo y puedes volver cuando quieras."
                ),
            ]
        ),
        ["subscription.cancellation_scheduled"] = new(
            "Tu suscripción a {{ product_name }} termina el {{ access_ends_date | office_date }}",
            "Tu suscripción va a terminar. Hasta entonces conservas el acceso completo.",
            [
                (">Billing<", ">Facturación<"),
                ("Your subscription will end", "Tu suscripción va a terminar"),
                (
                    """Hi <strong style="color:#23384B;">{{ first_name }}</strong>, we've scheduled the cancellation of your <strong style="color:#1E466B;">{{ plan_name }}</strong> plan. Nothing changes today: you keep full access until <strong style="color:#1E466B;">{{ access_ends_date | office_date }}</strong>, the end of the period you already paid for. We won't charge you again.""",
                    """Hola, <strong style="color:#23384B;">{{ first_name }}</strong>. Programamos la cancelación de tu plan <strong style="color:#1E466B;">{{ plan_name }}</strong>. Hoy no cambia nada: conservas el acceso completo hasta el <strong style="color:#1E466B;">{{ access_ends_date | office_date }}</strong>, el final del periodo que ya pagaste. No te volveremos a cobrar."""
                ),
                ("Keep my subscription", "Mantener mi suscripción"),
                ButtonFallbackEs,
                (
                    "Changed your mind? You can undo this any time before {{ access_ends_date | office_date }} and nothing will have happened. After that date your data stays safe — you can come back whenever you want.",
                    "¿Cambiaste de opinión? Puedes deshacerlo en cualquier momento antes del {{ access_ends_date | office_date }} y no habrá pasado nada. Después de esa fecha tus datos siguen a salvo: puedes volver cuando quieras."
                ),
            ]
        ),
        ["subscription.version_change_scheduled"] = new(
            "Las condiciones de tu plan de {{ product_name }} cambian el {{ change_date | office_date }}",
            "Las condiciones de tu plan cambian en tu próxima renovación. Esto es lo que cambia.",
            [
                (">Billing<", ">Facturación<"),
                ("Your plan terms will change", "Las condiciones de tu plan van a cambiar"),
                (
                    """Hi <strong style="color:#23384B;">{{ first_name }}</strong>, we're updating the terms of your <strong style="color:#1E466B;">{{ plan_name }}</strong> plan. Nothing changes today: the new terms start with your renewal on <strong style="color:#1E466B;">{{ change_date | office_date }}</strong>.""",
                    """Hola, <strong style="color:#23384B;">{{ first_name }}</strong>. Estamos actualizando las condiciones de tu plan <strong style="color:#1E466B;">{{ plan_name }}</strong>. Hoy no cambia nada: las nuevas condiciones empiezan con tu renovación del <strong style="color:#1E466B;">{{ change_date | office_date }}</strong>."""
                ),
                ("<strong>What changes:</strong>", "<strong>Qué cambia:</strong>"),
                ("Review my subscription", "Revisar mi suscripción"),
                ButtonFallbackEs,
                (
                    "Until {{ change_date | office_date }} you keep your current terms. If you'd rather change plans or cancel before then, you can do it from Manage subscription.",
                    "Hasta el {{ change_date | office_date }} conservas tus condiciones actuales. Si prefieres cambiar de plan o cancelar antes, puedes hacerlo desde Manage subscription."
                ),
            ]
        ),
        ["subscription.billing_cycle_changed"] = new(
            "Tu suscripción a {{ product_name }} ahora se factura " + CadenceEveryEs,
            null,
            [
                (">Billing<", ">Facturación<"),
                ("You're now on {{ cadence }}ly billing", "Ahora tienes facturación " + CadenceAdjectiveEs),
                (
                    """Hi <strong style="color:#23384B;">{{ first_name }}</strong>, your <strong style="color:#1E466B;">{{ plan_name }}</strong> plan, add-ons and extra seats are now billed every {{ cadence }}. We charged <strong style="color:#1E466B;">${{ amount }}</strong> on {{ date_charged | office_date }}; your receipt lists each item.""",
                    """Hola, <strong style="color:#23384B;">{{ first_name }}</strong>. Tu plan <strong style="color:#1E466B;">{{ plan_name }}</strong>, tus complementos y tus puestos adicionales ahora se facturan """
                        + CadenceEveryEs
                        + """. Cobramos <strong style="color:#1E466B;">${{ amount }}</strong> el {{ date_charged | office_date }}; tu recibo detalla cada concepto."""
                ),
                ("<strong>Next renewal:</strong>", "<strong>Próxima renovación:</strong>"),
                (
                    "Your billing will change to every {{ cadence }}",
                    "Tu facturación pasará a ser " + CadenceAdjectiveEs
                ),
                (
                    """Hi <strong style="color:#23384B;">{{ first_name }}</strong>, nothing changes today. Starting <strong style="color:#1E466B;">{{ next_charge_date | office_date }}</strong>, your <strong style="color:#1E466B;">{{ plan_name }}</strong> plan, add-ons and extra seats will be billed every {{ cadence }} at the current prices.""",
                    """Hola, <strong style="color:#23384B;">{{ first_name }}</strong>. Hoy no cambia nada. A partir del <strong style="color:#1E466B;">{{ next_charge_date | office_date }}</strong>, tu plan <strong style="color:#1E466B;">{{ plan_name }}</strong>, tus complementos y tus puestos adicionales se facturarán """
                        + CadenceEveryEs
                        + " a los precios actuales."
                ),
                ("Review my subscription", "Revisar mi suscripción"),
                ButtonFallbackEs,
            ]
        ),
        ["subscription.expired"] = new(
            "Tu suscripción a {{ product_name }} venció",
            "Tu suscripción venció. Renuévala cuando quieras.",
            [
                (">Billing<", ">Facturación<"),
                ("Your subscription has expired", "Tu suscripción venció"),
                (
                    """Hi <strong style="color:#23384B;">{{ first_name }}</strong>, your <strong style="color:#1E466B;">{{ plan_name }}</strong> plan has expired and access is now closed. Renew whenever you're ready to pick up right where you left off — your data is safe and waiting.""",
                    """Hola, <strong style="color:#23384B;">{{ first_name }}</strong>. Tu plan <strong style="color:#1E466B;">{{ plan_name }}</strong> venció y el acceso quedó cerrado. Renuévalo cuando quieras para seguir justo donde lo dejaste: tus datos están a salvo esperándote."""
                ),
                ("Renew subscription", "Renovar suscripción"),
                ButtonFallbackEs,
                (
                    "If you'd rather not renew, no action is needed. Your account will stay closed until you come back.",
                    "Si prefieres no renovar, no tienes que hacer nada. Tu cuenta seguirá cerrada hasta que vuelvas."
                ),
            ]
        ),
        ["subscription.renewal_upcoming"] = new(
            "Tu suscripción a {{ product_name }} se renueva en {{ days_until_renewal }} día(s)",
            "Tu suscripción se renueva pronto: no tienes que hacer nada, solo te avisamos.",
            [
                (">Billing<", ">Facturación<"),
                ("Your subscription renews soon", "Tu suscripción se renueva pronto"),
                (
                    """Hi <strong style="color:#23384B;">{{ first_name }}</strong>, just a heads up — your <strong style="color:#1E466B;">{{ plan_name }}</strong> plan renews on <strong style="color:#1E466B;">{{ renewal_date | office_date }}</strong> ({{ days_until_renewal }} day(s) from now). Nothing to do: we'll charge your card on file automatically.""",
                    """Hola, <strong style="color:#23384B;">{{ first_name }}</strong>. Solo te avisamos: tu plan <strong style="color:#1E466B;">{{ plan_name }}</strong> se renueva el <strong style="color:#1E466B;">{{ renewal_date | office_date }}</strong> (dentro de {{ days_until_renewal }} día(s)). No tienes que hacer nada: cobraremos automáticamente la tarjeta que tienes guardada."""
                ),
                (
                    "<strong>Amount:</strong> ${{ amount }} every {{ cadence }}, including your add-ons and extra seats.",
                    "<strong>Importe:</strong> ${{ amount }} "
                        + CadenceEveryEs
                        + ", con tus complementos y puestos adicionales incluidos."
                ),
                ("Manage subscription</", "Administrar suscripción</"),
                ButtonFallbackEs,
                (
                    "Want to make a change? You can switch plans, update your card or cancel from the dashboard before the renewal date.",
                    "¿Quieres hacer un cambio? Puedes cambiar de plan, actualizar tu tarjeta o cancelar desde el panel antes de la fecha de renovación."
                ),
            ]
        ),
        ["subscription.reactivated"] = new(
            "Tu suscripción a {{ product_name }} está activa de nuevo",
            "Todo listo: tu suscripción está activa de nuevo.",
            [
                (">Billing<", ">Facturación<"),
                ("You're all set — welcome back", "Todo listo: qué bueno tenerte de vuelta"),
                (
                    """Hi <strong style="color:#23384B;">{{ first_name }}</strong>, good news: your <strong style="color:#1E466B;">{{ plan_name }}</strong> plan is active again and full access has been restored for your whole team.""",
                    """Hola, <strong style="color:#23384B;">{{ first_name }}</strong>. Buenas noticias: tu plan <strong style="color:#1E466B;">{{ plan_name }}</strong> está activo de nuevo y todo tu equipo recuperó el acceso completo."""
                ),
                ("Go to your workspace", "Ir a tu espacio de trabajo"),
                (
                    "Thanks for staying with {{ product_name }}. If anything looks off, reach out to support anytime.",
                    "Gracias por seguir con {{ product_name }}. Si algo no se ve bien, escribe a soporte cuando quieras."
                ),
            ]
        ),
        ["subscription.recurring_charge"] = new(
            "Pago recibido: tu suscripción a {{ product_name }} continúa",
            "Pago recibido: tu suscripción continúa.",
            [
                (">Billing<", ">Facturación<"),
                ("Payment received</td>", "Pago recibido</td>"),
                (
                    """Hi <strong style="color:#23384B;">{{ first_name }}</strong>, we charged <strong style="color:#1E466B;">${{ amount }}</strong> to your {{ card_brand }} ending {{ card_last4 }} for <strong style="color:#1E466B;">{{ item_description }}</strong>.""",
                    """Hola, <strong style="color:#23384B;">{{ first_name }}</strong>. Cobramos <strong style="color:#1E466B;">${{ amount }}</strong> a tu {{ card_brand }} terminada en {{ card_last4 }} por <strong style="color:#1E466B;">{{ item_description }}</strong>."""
                ),
                (
                    """ The next charge is scheduled for <strong style="color:#23384B;">{{ next_charge_date | office_date }}</strong>.""",
                    """ El próximo cobro está programado para el <strong style="color:#23384B;">{{ next_charge_date | office_date }}</strong>."""
                ),
                (
                    """Includes your <strong style="color:#23384B;">{{ discount_label }}</strong> discount (&minus;${{ discount_amount }}).""",
                    """Incluye tu descuento de <strong style="color:#23384B;">{{ discount_label }}</strong> (&minus;${{ discount_amount }})."""
                ),
                ("View receipt", "Ver recibo"),
                ("Manage billing</a>", "Administrar facturación</a>"),
                (
                    "You turned on automatic renewal on this subscription. You can switch back to manual any time from Manage billing.",
                    "Activaste la renovación automática en esta suscripción. Puedes volver al pago manual cuando quieras desde Manage billing."
                ),
            ]
        ),
        ["subscription.recurring_charge_failed"] = new(
            "{% if is_final %}Último aviso: no pudimos procesar tu pago de {{ product_name }}{% else %}Acción requerida: no pudimos procesar tu pago de {{ product_name }}{% endif %}",
            "Tu pago no se completó. Mira qué pasa ahora y cómo mantener tu acceso.",
            [
                (">Billing<", ">Facturación<"),
                (
                    "{% if is_final %}Your payment couldn't be completed{% else %}We couldn't process your payment{% endif %}",
                    "{% if is_final %}Tu pago no se pudo completar{% else %}No pudimos procesar tu pago{% endif %}"
                ),
                (
                    """Hi <strong style="color:#23384B;">{{ first_name }}</strong>, {% if is_final %}we made our final attempt to charge{% else %}we attempted to charge{% endif %} {% if has_card %}your <strong style="color:#1E466B;">{{ card_brand }} ending in {{ card_last4 }}</strong>{% else %}your account{% endif %} for <strong style="color:#1E466B;">{{ item_description }}</strong>, but the payment was not completed.""",
                    """Hola, <strong style="color:#23384B;">{{ first_name }}</strong>. {% if is_final %}Hicimos el último intento de cobro{% else %}Intentamos cobrar{% endif %} {% if has_card %}a tu <strong style="color:#1E466B;">{{ card_brand }} terminada en {{ card_last4 }}</strong>{% else %}a tu cuenta{% endif %} por <strong style="color:#1E466B;">{{ item_description }}</strong>, pero el pago no se completó."""
                ),
                (
                    "As a result, <strong>{{ suspension_effect }}</strong>.",
                    "Como consecuencia, <strong>{{ suspension_effect }}</strong>."
                ),
                (">Amount</td>", ">Importe</td>"),
                (">Item</td>", ">Concepto</td>"),
                (">Attempt</td>", ">Intento</td>"),
                ("{{ attempt_number }} of {{ max_attempts }}", "{{ attempt_number }} de {{ max_attempts }}"),
                (">Reason</td>", ">Motivo</td>"),
                (">Next attempt</td>", ">Próximo intento</td>"),
                (
                    """{% if is_final %}Your data is safe and nothing has been deleted. To restore access right away, complete the payment in Manage subscription.{% else %}<strong style="color:#23384B;">What happens next:</strong> we'll automatically try again on {{ next_retry_date | office_date }}. To keep your team's access uninterrupted, please review your payment method or complete the payment now.{% endif %}""",
                    """{% if is_final %}Tus datos están a salvo y no se borró nada. Para recuperar el acceso de inmediato, completa el pago en Manage subscription.{% else %}<strong style="color:#23384B;">Qué pasa ahora:</strong> lo intentaremos de nuevo automáticamente el {{ next_retry_date | office_date }}. Para que tu equipo no pierda el acceso, revisa tu método de pago o completa el pago ahora.{% endif %}"""
                ),
                ("Go to Manage subscription", "Ir a Manage subscription"),
                (
                    "{% unless is_final %}If you've already taken care of this, no further action is needed. {% endunless %}Need a hand? Our support team is happy to help you get back on track.",
                    "{% unless is_final %}Si ya lo resolviste, no tienes que hacer nada más. {% endunless %}¿Necesitas ayuda? Nuestro equipo de soporte te ayuda con gusto a ponerte al día."
                ),
            ]
        ),
        ["subscription.billing_mode_changed"] = new(
            "Tu preferencia de facturación cambió a {% if new_mode == 'Automatic' %}renovación automática{% else %}pago manual{% endif %}",
            "Se actualizó tu preferencia de facturación.",
            [
                (">Billing<", ">Facturación<"),
                ("Billing preference updated", "Preferencia de facturación actualizada"),
                (
                    """Hi <strong style="color:#23384B;">{{ first_name }}</strong>, your billing preference on {{ product_name }} is now <strong style="color:#1E466B;">{{ new_mode }}</strong>.""",
                    """Hola, <strong style="color:#23384B;">{{ first_name }}</strong>. Tu preferencia de facturación en {{ product_name }} ahora es <strong style="color:#1E466B;">{% if new_mode == 'Automatic' %}renovación automática{% else %}pago manual{% endif %}</strong>."""
                ),
                (
                    " We'll renew your subscription automatically at the end of each period.",
                    " Renovaremos tu suscripción automáticamente al final de cada periodo."
                ),
                (
                    " We'll remind you to pay at the end of each period — nothing is charged until you do.",
                    " Te recordaremos pagar al final de cada periodo; no se cobra nada hasta que lo hagas."
                ),
                ("Manage billing</a>", "Administrar facturación</a>"),
                (
                    "Changed by mistake? Open Manage billing and switch back any time.",
                    "¿Lo cambiaste por error? Abre Manage billing y vuelve a cambiarlo cuando quieras."
                ),
            ]
        ),
        ["subscription.mandate_created"] = new(
            "Se activó la renovación automática de tu suscripción a {{ product_name }}",
            "La renovación automática ya está activa en tu suscripción.",
            [
                (">Billing<", ">Facturación<"),
                ("Automatic renewal enabled", "Renovación automática activada"),
                (
                    """Hi <strong style="color:#23384B;">{{ first_name }}</strong>, we'll automatically charge your {{ card_brand }} ending {{ card_last4 }} <strong style="color:#1E466B;">${{ amount }}</strong> every {{ cadence }} for your <strong style="color:#1E466B;">{{ plan_name }}</strong> plan.""",
                    """Hola, <strong style="color:#23384B;">{{ first_name }}</strong>. Cobraremos automáticamente <strong style="color:#1E466B;">${{ amount }}</strong> """
                        + CadenceEveryEs
                        + """ a tu {{ card_brand }} terminada en {{ card_last4 }} por tu plan <strong style="color:#1E466B;">{{ plan_name }}</strong>."""
                ),
                (
                    "You'll get a receipt every time we charge, and you can switch to manual any time.",
                    "Recibirás un recibo cada vez que cobremos y puedes cambiar al pago manual cuando quieras."
                ),
                ("Manage billing</a>", "Administrar facturación</a>"),
            ]
        ),
        ["wallet.low_balance"] = new(
            "El saldo de tu Wallet está bajo",
            "Tu Wallet está por debajo de tu umbral de aviso: agrega fondos para que los mensajes se sigan enviando.",
            [
                ("Your wallet balance is low", "El saldo de tu Wallet está bajo"),
                (
                    """Your office's Wallet has <strong style="color:#23384B;">{{ available_label }}</strong> left, below your alert threshold of <strong style="color:#23384B;">{{ threshold_label }}</strong>.""",
                    """Al Wallet de tu oficina le quedan <strong style="color:#23384B;">{{ available_label }}</strong>, por debajo de tu umbral de aviso de <strong style="color:#23384B;">{{ threshold_label }}</strong>."""
                ),
                (
                    "Add funds so text messages and campaigns keep sending without interruption.",
                    "Agrega fondos para que los mensajes de texto y las campañas se sigan enviando sin interrupciones."
                ),
                ("Add funds</a>", "Agregar fondos</a>"),
                (
                    "You can change the alert threshold or turn on automatic recharge in ",
                    "Puedes cambiar el umbral de aviso o activar la recarga automática en "
                ),
                WalletAudienceEs,
            ]
        ),
        ["wallet.auto_recharge_succeeded"] = new(
            "Recibo: recarga automática de {{ amount_label }}",
            "Recargamos tu Wallet automáticamente. Aquí tienes tu recibo.",
            [
                ("Automatic recharge completed", "Recarga automática completada"),
                (
                    """Your Wallet balance went below your threshold, so we added <strong style="color:#23384B;">{{ amount_label }}</strong> using your saved payment method.""",
                    """El saldo de tu Wallet bajó de tu umbral, así que agregamos <strong style="color:#23384B;">{{ amount_label }}</strong> con tu método de pago guardado."""
                ),
                (">Amount</td>", ">Importe</td>"),
                (">Payment method</td>", ">Método de pago</td>"),
                (">Balance after recharge</td>", ">Saldo después de la recarga</td>"),
                (">Date</td>", ">Fecha</td>"),
                ("View your Wallet", "Ver tu Wallet"),
                (
                    "You turned on automatic recharge. You can change it or turn it off any time in ",
                    "Activaste la recarga automática. Puedes cambiarla o desactivarla cuando quieras en "
                ),
                WalletAudienceEs,
            ]
        ),
        ["wallet.auto_recharge_paused"] = new(
            "La recarga automática está en pausa",
            "La recarga automática está en pausa y no se reintentará sola. Revísala en Settings.",
            [
                ("Automatic recharge is paused", "La recarga automática está en pausa"),
                (
                    """We paused automatic recharge, and it won't retry on its own. Review your payment method in <strong style="color:#23384B;">Settings &rarr; Wallet</strong> and turn it back on.""",
                    """Pausamos la recarga automática y no se reintentará sola. Revisa tu método de pago en <strong style="color:#23384B;">Settings &rarr; Wallet</strong> y vuelve a activarla."""
                ),
                (">Recharge amount</td>", ">Monto de la recarga</td>"),
                (">Payment method</td>", ">Método de pago</td>"),
                ("Review Wallet settings", "Revisar la configuración del Wallet"),
                (
                    "Until then, your balance won't be topped up automatically. ",
                    "Mientras tanto, tu saldo no se recargará automáticamente. "
                ),
                WalletAudienceEs,
            ]
        ),
        ["wallet.auto_recharge_daily_limit"] = new(
            "Se alcanzó el límite diario de recargas automáticas",
            "Se alcanzó el límite de recargas automáticas de hoy. Sigue mañana.",
            [
                ("Daily automatic recharge limit reached", "Se alcanzó el límite diario de recargas automáticas"),
                (
                    "Your Wallet was recharged {{ daily_limit }} times today ({{ amount_label }} each). That's the most allowed per day, so it won't recharge again today.",
                    "Tu Wallet se recargó {{ daily_limit }} veces hoy ({{ amount_label }} cada vez). Es el máximo permitido por día, así que hoy no se volverá a recargar."
                ),
                (
                    "Automatic recharge isn't paused: it continues tomorrow (your office's time). If you need funds today, add them from your Wallet.",
                    "La recarga automática no está en pausa: sigue mañana (hora de tu oficina). Si necesitas fondos hoy, agrégalos desde tu Wallet."
                ),
                ("Review Wallet settings", "Revisar la configuración del Wallet"),
                (
                    "Up to {{ daily_limit }} automatic recharges per day is the limit you agreed to. ",
                    "Hasta {{ daily_limit }} recargas automáticas por día es el límite que aceptaste. "
                ),
                WalletAudienceEs,
            ]
        ),
        // ---------- Avisos obligatorios a la oficina (D-N48) ----------
        ["payments.payout_failed.v1"] = new(
            "Acción requerida: falló un depósito de {{ amount_label }}",
            "Falló un depósito a la cuenta bancaria de tu oficina. Revisa tus datos bancarios.",
            [
                OnlinePaymentsEyebrowEs,
                ("A payout failed", "Falló un depósito"),
                (
                    """We couldn't send <strong style="color:#23384B;">{{ amount_label }}</strong> to your office's bank account.""",
                    """No pudimos depositar <strong style="color:#23384B;">{{ amount_label }}</strong> en la cuenta bancaria de tu oficina."""
                ),
                (">Amount<", ">Monto<"),
                DateLabelEs,
                (">Reason<", ">Motivo<"),
                (">Reference<", ">Referencia<"),
                (
                    "Check the bank account connected to your payment provider and update it if needed.",
                    "Revisa la cuenta bancaria conectada a tu proveedor de pagos y actualízala si hace falta."
                ),
                ReviewOnlinePaymentsEs,
                PaymentsAudienceEs,
            ]
        ),
        ["payments.connect_account_restricted.v1"] = new(
            "Acción requerida: tu cuenta de cobros está restringida",
            "Los cobros en línea de tu oficina están en pausa. Envía la información que falta para reactivarlos.",
            [
                OnlinePaymentsEyebrowEs,
                ("Your payments account is restricted", "Tu cuenta de cobros está restringida"),
                (
                    "Your payment provider paused online payments for your office. Clients can't pay online until you provide the information it asks for.",
                    "Tu proveedor de pagos pausó los cobros en línea de tu oficina. Tus clientes no podrán pagar en línea hasta que envíes la información que pide."
                ),
                DateLabelEs,
                PendingItemsEs,
                ReviewOnlinePaymentsEs,
                PaymentsAudienceEs,
            ]
        ),
        ["payments.connect_account_onboarding_required.v1"] = new(
            "Termina de configurar tu cuenta de cobros",
            "Tu proveedor de pagos necesita algunos datos más antes de que puedas aceptar cobros en línea.",
            [
                OnlinePaymentsEyebrowEs,
                ("Finish setting up your payments account", "Termina de configurar tu cuenta de cobros"),
                (
                    "Your payment provider needs a few more details before your office can accept online payments.",
                    "Tu proveedor de pagos necesita algunos datos más antes de que tu oficina pueda aceptar cobros en línea."
                ),
                PendingItemsEs,
                ("Continue setup", "Continuar la configuración"),
                PaymentsAudienceEs,
            ]
        ),
        ["storage.file_infected.v1"] = new(
            "Alerta de seguridad: se bloqueó un archivo con malware",
            "Encontramos un archivo con malware y lo pusimos en cuarentena.",
            [
                (">Storage<", ">Almacenamiento<"),
                ("A file with malware was blocked", "Se bloqueó un archivo con malware"),
                (
                    "Our virus scan found malware in a file uploaded to your office. We moved it to quarantine, so no one can open or download it.",
                    "Nuestro antivirus encontró malware en un archivo que se subió a tu oficina. Lo pusimos en cuarentena, así que nadie puede abrirlo ni descargarlo."
                ),
                DateLabelEs,
                (">Scan result<", ">Resultado del análisis<"),
                (
                    "The rest of your files stay available. If you know who uploaded it, let them know and ask for a clean copy.",
                    "El resto de tus archivos sigue disponible. Si sabes quién lo subió, avísale y pídele una copia limpia."
                ),
                ReviewTheFileEs,
                (
                    "You're receiving this because you manage your office's storage on {{ product_name }}.",
                    "Recibes este correo porque administras el almacenamiento de tu oficina en {{ product_name }}."
                ),
            ]
        ),
        ["storage.dmca_takedown.v1"] = new(
            "Urgente: se bloqueó un archivo por un aviso DMCA",
            "Se bloqueó un archivo por un aviso DMCA. Revísalo cuanto antes.",
            [
                ("A file was blocked by a DMCA notice", "Se bloqueó un archivo por un aviso DMCA"),
                (
                    "A file in your office was blocked after a copyright (DMCA) notice. It stays in storage, but no one can open, download or share it.",
                    "Se bloqueó un archivo de tu oficina por un aviso de derechos de autor (DMCA). Sigue guardado, pero nadie puede abrirlo, descargarlo ni compartirlo."
                ),
                DateLabelEs,
                NoticeReferenceEs,
                (
                    "Review the notice. If you believe it's a mistake, you can submit a counter-notice.",
                    "Revisa el aviso. Si crees que es un error, puedes presentar una contranotificación."
                ),
                ReviewTheFileEs,
                LegalAudienceEs,
            ]
        ),
        ["storage.dmca_counter_notice.v1"] = new(
            "Se presentó una contranotificación para un archivo bloqueado",
            "Se presentó una contranotificación para un archivo bloqueado por un aviso DMCA. Revísala.",
            [
                ("A DMCA counter-notice was submitted", "Se presentó una contranotificación DMCA"),
                (
                    "Someone disputed the DMCA notice that blocked a file in your office. The file stays blocked until it's restored.",
                    "Alguien disputó el aviso DMCA que bloqueó un archivo de tu oficina. El archivo sigue bloqueado hasta que se restaure."
                ),
                DateLabelEs,
                NoticeReferenceEs,
                (
                    "Review the counter-notice and decide whether to restore the file.",
                    "Revisa la contranotificación y decide si restauras el archivo."
                ),
                ReviewTheFileEs,
                LegalAudienceEs,
            ]
        ),
        ["storage.dmca_reinstated.v1"] = new(
            "Se restauró un archivo bloqueado por un aviso DMCA",
            "Un archivo bloqueado por un aviso DMCA vuelve a estar disponible.",
            [
                ("File restored", "Archivo restaurado"),
                (
                    "A file blocked by a DMCA notice was restored. Your team can open, download and share it again.",
                    "Se restauró un archivo bloqueado por un aviso DMCA. Tu equipo puede volver a abrirlo, descargarlo y compartirlo."
                ),
                DateLabelEs,
                NoticeReferenceEs,
                OpenTheFileEs,
                LegalAudienceEs,
            ]
        ),
        ["storage.legal_hold_placed.v1"] = new(
            "Un archivo quedó en retención legal",
            "Un archivo está en retención legal y no se puede borrar hasta que se levante.",
            [
                ("A file was placed on legal hold", "Un archivo quedó en retención legal"),
                (
                    "A file in your office was placed on legal hold. While the hold is active, the file can't be deleted or expire.",
                    "Un archivo de tu oficina quedó en retención legal. Mientras siga activa, el archivo no se puede borrar ni vencer."
                ),
                DateLabelEs,
                (">Reason<", ">Motivo<"),
                ReviewTheFileEs,
                LegalAudienceEs,
            ]
        ),
        ["storage.legal_hold_lifted.v1"] = new(
            "Se levantó la retención legal de un archivo",
            "Un archivo ya no está en retención legal.",
            [
                ("Legal hold lifted", "Retención legal levantada"),
                (
                    "The legal hold on a file in your office was lifted. The file follows your office's usual retention rules again, so it can be deleted or expire.",
                    "Se levantó la retención legal de un archivo de tu oficina. El archivo vuelve a seguir las reglas de retención habituales de tu oficina, así que se puede borrar o vencer."
                ),
                DateLabelEs,
                OpenTheFileEs,
                LegalAudienceEs,
            ]
        ),
    };

    // Piezas de los avisos obligatorios a la oficina.
    private static (string, string) OnlinePaymentsEyebrowEs => (">Online payments<", ">Cobros en línea<");

    private static (string, string) ReviewOnlinePaymentsEs => ("Review online payments", "Revisar cobros en línea");

    private static (string, string) PaymentsAudienceEs =>
        (
            "You're receiving this because you manage your office's online payments on {{ product_name }}.",
            "Recibes este correo porque administras los cobros en línea de tu oficina en {{ product_name }}."
        );

    private static (string, string) LegalAudienceEs =>
        (
            "You're receiving this because you handle legal matters for your office's files on {{ product_name }}.",
            "Recibes este correo porque te encargas de los asuntos legales de los archivos de tu oficina en {{ product_name }}."
        );

    private static (string, string) DateLabelEs => (">Date<", ">Fecha<");

    private static (string, string) PendingItemsEs => (">Pending items<", ">Pendientes<");

    private static (string, string) NoticeReferenceEs => (">Notice reference<", ">Referencia del aviso<");

    private static (string, string) ReviewTheFileEs => ("Review the file", "Revisar el archivo");

    private static (string, string) OpenTheFileEs => ("Open the file", "Abrir el archivo");
}
