using TaxVision.Scribe.Domain.Templates;

namespace TaxVision.Scribe.Application.Templates.Seed;

/// <summary>
/// Correos de la demo pública del landing ("Book a live demo", DEMO-09 §7). Inglés por defecto y español con
/// <c>language == 'Es'</c> en los del visitante; los del anfitrión, en inglés (equipo de la plataforma). Las
/// horas llegan ya formateadas en la zona de cada persona (DEMO-10). La marca sale de <c>product_name</c>.
/// </summary>
public static partial class NotificationTemplateSeedSource
{
    /// <summary>v30 (por plantilla): las 6 plantillas nuevas de la demo pública.</summary>
    private const int PublicDemoVersion = 30;

    /// <summary>v31 (por plantilla): confirmación con "Add to calendar" como acción principal y el aviso
    /// "ya tienes una demo" (DEMO-11).</summary>
    private const int PublicDemoCtaVersion = 31;

    // ---------- Piezas de la cáscara interna (mismos estilos que las plantillas de cita) ----------

    private static string DemoEyebrow(string text) =>
        $"""<tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">{text}</td></tr>"""
        + """<tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>""";

    private static string DemoTitle(string text) =>
        $"""<tr><td style="padding-bottom:16px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">{text}</td></tr>""";

    private static string DemoParagraph(string text) =>
        $"""<tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">{text}</td></tr>""";

    private static string DemoNote(string text) =>
        $"""<tr><td style="padding-top:8px;font-family:Arial,Helvetica,sans-serif;font-size:13px;line-height:20px;color:#70869A;mso-line-height-rule:exactly;">{text}</td></tr>""";

    private static string DemoBox(string text) =>
        $"""<tr><td style="padding-bottom:12px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF" style="background-color:#EAF4FF;border-radius:10px;"><tr><td style="padding:14px 18px;border-left:3px solid #67BAF4;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:22px;color:#23384B;mso-line-height-rule:exactly;">{text}</td></tr></table></td></tr>""";

    private static string DemoButton(string url, string label) =>
        $"""<tr><td align="left" style="padding:8px 0 8px 0;"><table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{url}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">{label}</a></td></tr></table></td></tr>""";

    private static string DemoLink(string url, string label) =>
        $"""<a href="{url}" target="_blank" style="color:#1E466B;font-weight:bold;text-decoration:underline;">{label}</a>""";

    private static string DemoWhen =>
        "<strong>{{ date_local }}</strong> · <strong>{{ time_local }}</strong> <span style=\"color:#70869A;\">({{ time_zone_label }})</span>";

    private static string DemoDetail(string label, string value) =>
        $"""<tr><td style="padding:4px 0;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#496174;mso-line-height-rule:exactly;"><span style="color:#70869A;">{label}:</span> <strong style="color:#23384B;">{value}</strong></td></tr>""";

    private static string DemoManageFooter =>
        DemoNote(
            Bilingual(
                "¿Necesitas otro horario? " + DemoLink("{{ manage_url }}", "Cámbiala o cancélala aquí") + ".",
                "Need a different time? " + DemoLink("{{ manage_url }}", "Reschedule or cancel here") + "."
            )
        );

    private static string DemoTable(params string[] rows) =>
        """<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">"""
        + string.Concat(rows)
        + "</table>";

    // ---------- Variables comunes ----------

    private static readonly (
        string Name,
        VariableType Type,
        bool Required,
        string? DefaultValue,
        string? Description
    )[] DemoCommonVariables =
    [
        ("language", VariableType.String, true, "En", "Language of the reader: En or Es."),
        ("is_host", VariableType.Bool, false, "false", "True when the email goes to the demo host."),
        ("recipient_email", VariableType.String, false, null, "Who receives this email."),
        ("first_name", VariableType.String, false, null, "First name of the reader."),
        ("page_title", VariableType.String, false, null, "Title of the public booking page."),
        (
            "product_name",
            VariableType.String,
            false,
            "TaxOffix",
            "Product brand (the renderer injects it from PlatformSettings)."
        ),
    ];

    private static readonly (
        string Name,
        VariableType Type,
        bool Required,
        string? DefaultValue,
        string? Description
    )[] DemoWhenVariables =
    [
        ("host_name", VariableType.String, true, null, "Who gives the demo."),
        ("date_local", VariableType.String, true, null, "Demo date in the reader's time zone and language."),
        ("time_local", VariableType.String, true, null, "Demo start time in the reader's time zone."),
        ("time_zone_label", VariableType.String, true, null, "Readable time zone of the reader, e.g. New York time."),
        ("duration_minutes", VariableType.Number, false, "30", "Demo length in minutes."),
        ("meeting_url", VariableType.Url, true, null, "Host's meeting link (Meet, Zoom or Teams)."),
    ];

    private static readonly (
        string Name,
        VariableType Type,
        bool Required,
        string? DefaultValue,
        string? Description
    )[] DemoVisitorVariables =
    [
        ("manage_url", VariableType.Url, false, null, "Link to reschedule or cancel (carries the visitor's secret)."),
        ("google_calendar_url", VariableType.Url, false, null, "Add to Google Calendar link."),
        ("outlook_calendar_url", VariableType.Url, false, null, "Add to Outlook link."),
    ];

    private static readonly (
        string Name,
        VariableType Type,
        bool Required,
        string? DefaultValue,
        string? Description
    )[] DemoProspectVariables =
    [
        ("visitor_name", VariableType.String, false, null, "Prospect full name."),
        ("visitor_email", VariableType.String, false, null, "Prospect work email."),
        ("company", VariableType.String, false, null, "Prospect company."),
        ("office_size", VariableType.String, false, null, "Office size answer."),
        ("phone", VariableType.String, false, null, "Prospect phone."),
        ("interest", VariableType.String, false, null, "What the prospect wants to see."),
        ("visitor_time_local", VariableType.String, false, null, "Start time in the prospect's time zone."),
    ];

    // ---------- Plantillas ----------

    private static NotificationTemplateSeed PublicDemoVerificationCode { get; } =
        new(
            EventKey: "demo.verification_code.v1",
            TemplateKey: "demo.verification_code.v1",
            Name: "Public demo — Verification code",
            Subject: Bilingual(
                "Tu código de verificación de {{ product_name }}: {{ code }}",
                "Your {{ product_name }} verification code: {{ code }}"
            ),
            Html: DemoTable(
                DemoEyebrow(Bilingual("Agenda una demo", "Book a live demo")),
                DemoTitle(Bilingual("Confirma tu correo", "Confirm your email")),
                DemoParagraph(
                    Bilingual(
                        "Hola {{ first_name }}, usa este código para confirmar tu demo:",
                        "Hi {{ first_name }}, use this code to confirm your demo:"
                    )
                ),
                """<tr><td style="padding:6px 0 18px 0;"><table role="presentation" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF" style="background-color:#EAF4FF;border-radius:10px;"><tr><td style="padding:16px 28px;font-family:'Courier New',Courier,monospace;font-size:30px;line-height:36px;font-weight:bold;letter-spacing:8px;color:#1E466B;mso-line-height-rule:exactly;">{{ code }}</td></tr></table></td></tr>""",
                DemoParagraph(
                    Bilingual(
                        "Vence en {{ expires_minutes }} minutos. Hasta entonces tu horario queda apartado.",
                        "It expires in {{ expires_minutes }} minutes. Your time is held until then."
                    )
                ),
                DemoNote(
                    Bilingual(
                        "Si no pediste una demo, puedes ignorar este correo.",
                        "If you didn't request a demo, you can ignore this email."
                    )
                )
            ),
            Variables:
            [
                .. DemoCommonVariables,
                ("code", VariableType.String, true, null, "6-digit verification code."),
                ("expires_minutes", VariableType.Number, true, "10", "Minutes until the code and the hold expire."),
            ],
            ContentVersion: PublicDemoVersion
        );

    private static NotificationTemplateSeed PublicDemoConfirmed { get; } =
        new(
            EventKey: "demo.confirmed.v1",
            TemplateKey: "demo.confirmed.v1",
            Name: "Public demo — Confirmed (visitor)",
            Subject: Bilingual(
                "Tu demo de {{ product_name }} quedó confirmada para el {{ date_local }}",
                "Your {{ product_name }} demo is confirmed for {{ date_local }}"
            ),
            Html: DemoTable(
                DemoEyebrow(Bilingual("Demo confirmada", "Demo confirmed")),
                DemoTitle(Bilingual("¡Listo, {{ first_name }}!", "You're all set, {{ first_name }}!")),
                DemoParagraph(
                    Bilingual(
                        "Tu demo de {{ duration_minutes }} minutos con <strong style=\"color:#1E466B;\">{{ host_name }}</strong> quedó confirmada.",
                        "Your {{ duration_minutes }}-minute demo with <strong style=\"color:#1E466B;\">{{ host_name }}</strong> is confirmed."
                    )
                ),
                DemoBox(DemoWhen),
                // El día de la confirmación la acción es agendarla; entrar es el día de la demo (DEMO-11).
                DemoButton(
                    "{{ google_calendar_url }}",
                    Bilingual("Agregar a Google Calendar", "Add to Google Calendar")
                ),
                DemoParagraph(
                    DemoLink("{{ outlook_calendar_url }}", Bilingual("Agregar a Outlook", "Add to Outlook"))
                        + " · "
                        + Bilingual(
                            "o abre el archivo .ics adjunto en cualquier calendario.",
                            "or open the attached .ics file in any calendar."
                        )
                ),
                DemoNote(
                    Bilingual("Para entrar el día de la demo: ", "To join on the day: ")
                        + DemoLink("{{ meeting_url }}", Bilingual("Entrar a la reunión", "Join the meeting"))
                ),
                DemoManageFooter
            ),
            Variables: [.. DemoCommonVariables, .. DemoWhenVariables, .. DemoVisitorVariables],
            ContentVersion: PublicDemoCtaVersion
        );

    private static NotificationTemplateSeed PublicDemoNewBooking { get; } =
        new(
            EventKey: "demo.new_booking.v1",
            TemplateKey: "demo.new_booking.v1",
            Name: "Public demo — New booking (host)",
            Subject: "New demo booked: {{ company }} · {{ date_local }} {{ time_local }}",
            Html: DemoTable(
                DemoEyebrow("New demo"),
                DemoTitle("{{ visitor_name }} booked a demo"),
                DemoBox(DemoWhen + "<br><span style=\"color:#70869A;\">Their time: {{ visitor_time_local }}</span>"),
                DemoDetail("Name", "{{ visitor_name }}"),
                DemoDetail("Email", "{{ visitor_email }}"),
                DemoDetail("Company", "{{ company }}"),
                "{% if office_size != blank %}" + DemoDetail("Office size", "{{ office_size }}") + "{% endif %}",
                "{% if phone != blank %}" + DemoDetail("Phone", "{{ phone }}") + "{% endif %}",
                "{% if interest != blank %}" + DemoDetail("Wants to see", "{{ interest }}") + "{% endif %}",
                DemoDetail(
                    "How they found us",
                    "{{ heard_about }}{% if heard_about_detail != blank %} — {{ heard_about_detail }}{% endif %}"
                ),
                DemoDetail(
                    "Source",
                    "{{ entry_source }}{% if utm_source != blank %} · {{ utm_source }}{% if utm_campaign != blank %}/{{ utm_campaign }}{% endif %}{% endif %}{% if referrer_domain != blank %} · from {{ referrer_domain }}{% endif %}"
                ),
                DemoDetail("Product emails", "{% if marketing_opt_in %}Yes{% else %}No{% endif %}"),
                DemoButton("{{ meeting_url }}", "Open the meeting link"),
                DemoNote("The calendar file (.ics) is attached.")
            ),
            Variables:
            [
                .. DemoCommonVariables,
                .. DemoWhenVariables,
                .. DemoProspectVariables,
                (
                    "heard_about",
                    VariableType.String,
                    true,
                    null,
                    "Answer to How did you hear about us? (English label)."
                ),
                ("heard_about_detail", VariableType.String, false, null, "Free text when the answer allows it."),
                ("entry_source", VariableType.String, true, null, "Landing button that opened the booking."),
                ("utm_source", VariableType.String, false, null, "utm_source of the visit."),
                ("utm_campaign", VariableType.String, false, null, "utm_campaign of the visit."),
                ("referrer_domain", VariableType.String, false, null, "Only the referrer domain."),
                (
                    "marketing_opt_in",
                    VariableType.Bool,
                    false,
                    "false",
                    "Whether the prospect accepted product emails."
                ),
            ],
            ContentVersion: PublicDemoVersion
        );

    private static NotificationTemplateSeed PublicDemoRescheduled { get; } =
        new(
            EventKey: "demo.rescheduled.v1",
            TemplateKey: "demo.rescheduled.v1",
            Name: "Public demo — Rescheduled",
            Subject: "{% if is_host %}Demo moved: {{ visitor_name }} · {{ date_local }} {{ time_local }}{% elsif language == 'Es' %}Tu demo de {{ product_name }} cambió al {{ date_local }}{% else %}Your {{ product_name }} demo was moved to {{ date_local }}{% endif %}",
            Html: DemoTable(
                DemoEyebrow(
                    "{% if is_host %}Demo moved{% else %}"
                        + Bilingual("Demo actualizada", "Demo updated")
                        + "{% endif %}"
                ),
                DemoTitle(
                    "{% if is_host %}The demo with {{ visitor_name }} has a new time{% else %}"
                        + Bilingual("Tu demo tiene un nuevo horario", "Your demo has a new time")
                        + "{% endif %}"
                ),
                """<tr><td style="padding-bottom:10px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#70869A;mso-line-height-rule:exactly;">"""
                    + Bilingual("Antes", "Before")
                    + ": <s>{{ previous_date_local }} · {{ previous_time_local }}</s></td></tr>",
                DemoBox(Bilingual("Ahora", "Now") + ": " + DemoWhen),
                DemoParagraph(
                    "{% if changed_by == 'Staff' %}"
                        + Bilingual("Lo cambió nuestro equipo", "Changed by our team")
                        + "{% else %}"
                        + "{% if is_host %}Changed by the visitor{% else %}"
                        + Bilingual("Lo cambiaste tú", "You changed it")
                        + "{% endif %}{% endif %}"
                        + "{% if reason != blank %}: {{ reason }}{% else %}.{% endif %}"
                ),
                DemoButton(
                    "{{ meeting_url }}",
                    "{% if is_host %}Open the meeting link{% else %}"
                        + Bilingual("Entrar a la reunión", "Join the meeting")
                        + "{% endif %}"
                ),
                "{% if is_host %}" + DemoNote("The calendar entry is updated in the attached file.") + "{% else %}",
                DemoNote(
                    Bilingual(
                        "Tu calendario se actualiza con el archivo adjunto.",
                        "The attached file updates your calendar."
                    )
                ),
                "{% if manage_url != blank %}" + DemoManageFooter + "{% endif %}",
                "{% endif %}"
            ),
            Variables:
            [
                .. DemoCommonVariables,
                .. DemoWhenVariables,
                .. DemoVisitorVariables,
                .. DemoProspectVariables,
                ("previous_date_local", VariableType.String, true, null, "Old date, in the reader's time zone."),
                ("previous_time_local", VariableType.String, true, null, "Old start time, in the reader's time zone."),
                ("changed_by", VariableType.String, true, null, "Visitor or Staff."),
                ("reason", VariableType.String, false, null, "Why it moved, when the team gave a reason."),
            ],
            ContentVersion: PublicDemoVersion
        );

    private static NotificationTemplateSeed PublicDemoCancelled { get; } =
        new(
            EventKey: "demo.cancelled.v1",
            TemplateKey: "demo.cancelled.v1",
            Name: "Public demo — Canceled",
            Subject: "{% if is_host %}Demo canceled: {{ visitor_name }} · {{ date_local }}{% elsif language == 'Es' %}Tu demo de {{ product_name }} fue cancelada{% else %}Your {{ product_name }} demo was canceled{% endif %}",
            Html: DemoTable(
                DemoEyebrow(
                    "{% if is_host %}Demo canceled{% else %}"
                        + Bilingual("Demo cancelada", "Demo canceled")
                        + "{% endif %}"
                ),
                DemoTitle(
                    "{% if is_host %}The demo with {{ visitor_name }} was canceled{% else %}"
                        + Bilingual("Tu demo fue cancelada", "Your demo was canceled")
                        + "{% endif %}"
                ),
                DemoBox(DemoWhen),
                DemoParagraph(
                    "{% if cancelled_by == 'Staff' %}"
                        + "{% if is_host %}Canceled by the team{% else %}"
                        + Bilingual("La canceló nuestro equipo", "Our team canceled it")
                        + "{% endif %}"
                        + "{% else %}"
                        + "{% if is_host %}Canceled by the visitor{% else %}"
                        + Bilingual("La cancelaste tú", "You canceled it")
                        + "{% endif %}{% endif %}"
                        + "{% if reason != blank %}: {{ reason }}{% else %}.{% endif %}"
                ),
                "{% if is_host %}" + DemoNote("The calendar file removes it from your calendar.") + "{% else %}",
                DemoParagraph(
                    Bilingual(
                        "Si todavía quieres conocer {{ product_name }}, agenda otro horario cuando te quede bien.",
                        "If you'd still like to see {{ product_name }}, book another time that works for you."
                    )
                ),
                "{% if book_again_url != blank %}"
                    + DemoButton("{{ book_again_url }}", Bilingual("Agendar otra demo", "Book another time"))
                    + "{% endif %}",
                "{% endif %}"
            ),
            Variables:
            [
                .. DemoCommonVariables,
                .. DemoWhenVariables,
                .. DemoVisitorVariables,
                .. DemoProspectVariables,
                ("cancelled_by", VariableType.String, true, null, "Visitor or Staff."),
                ("reason", VariableType.String, false, null, "Why it was canceled, when given."),
                ("book_again_url", VariableType.Url, false, null, "Public demo page, to book again."),
            ],
            ContentVersion: PublicDemoVersion
        );

    private static NotificationTemplateSeed PublicDemoAlreadyBooked { get; } =
        new(
            EventKey: "demo.already_booked.v1",
            TemplateKey: "demo.already_booked.v1",
            Name: "Public demo — Already booked (visitor)",
            Subject: Bilingual(
                "Ya tienes una demo de {{ product_name }} el {{ date_local }}",
                "You already have a {{ product_name }} demo on {{ date_local }}"
            ),
            Html: DemoTable(
                DemoEyebrow(Bilingual("Tu demo", "Your demo")),
                DemoTitle(Bilingual("Ya tienes una demo agendada", "You already have a demo booked")),
                DemoParagraph(
                    Bilingual(
                        "Hola {{ first_name }}, recibimos otro pedido con tu email. Tu demo con <strong style=\"color:#1E466B;\">{{ host_name }}</strong> sigue en pie:",
                        "Hi {{ first_name }}, we got another request with your email. Your demo with <strong style=\"color:#1E466B;\">{{ host_name }}</strong> is still on:"
                    )
                ),
                DemoBox(DemoWhen),
                DemoButton("{{ manage_url }}", Bilingual("Cambiar o cancelar la demo", "Reschedule or cancel")),
                DemoNote(
                    Bilingual(
                        "Este enlace reemplaza a los anteriores. Si no fuiste tú, no tienes que hacer nada.",
                        "This link replaces the previous ones. If this wasn't you, you don't need to do anything."
                    )
                ),
                DemoManageFooter
            ),
            Variables: [.. DemoCommonVariables, .. DemoWhenVariables, .. DemoVisitorVariables],
            ContentVersion: PublicDemoCtaVersion
        );

    private static NotificationTemplateSeed PublicDemoReminder { get; } =
        new(
            EventKey: "demo.reminder.v1",
            TemplateKey: "demo.reminder.v1",
            Name: "Public demo — Reminder",
            Subject: Bilingual(
                "Recordatorio: tu demo de {{ product_name }} es el {{ date_local }} a las {{ time_local }}",
                "Reminder: your {{ product_name }} demo is on {{ date_local }} at {{ time_local }}"
            ),
            Html: DemoTable(
                DemoEyebrow(Bilingual("Recordatorio", "Reminder")),
                DemoTitle(Bilingual("Tu demo se acerca", "Your demo is coming up")),
                DemoParagraph(
                    Bilingual(
                        "Hola {{ first_name }}, te esperamos para tu demo con <strong style=\"color:#1E466B;\">{{ host_name }}</strong>.",
                        "Hi {{ first_name }}, we look forward to your demo with <strong style=\"color:#1E466B;\">{{ host_name }}</strong>."
                    )
                ),
                DemoBox(DemoWhen),
                DemoButton("{{ meeting_url }}", Bilingual("Entrar a la reunión", "Join the meeting")),
                DemoManageFooter
            ),
            Variables:
            [
                .. DemoCommonVariables,
                .. DemoWhenVariables,
                .. DemoVisitorVariables,
                ("hours_before", VariableType.Number, false, "24", "How long before the demo this reminder goes out."),
            ],
            ContentVersion: PublicDemoVersion
        );
}
