using TaxVision.Scribe.Domain.Templates;

namespace TaxVision.Scribe.Application.Templates.Seed;

/// <summary>
/// D-N48: correos de los avisos obligatorios a la oficina que el catálogo permitía y no salían (depósito fallido,
/// cuenta de cobros, malware, DMCA y retención legal). Solo usan lo que ya trae el evento; las fechas llegan
/// tipadas y Scribe las imprime en la zona de la oficina. El español vive en <c>SpanishVersions</c>.
/// </summary>
public static partial class NotificationTemplateSeedSource
{
    /// <summary>v38 (por plantilla): las 8 plantillas de avisos obligatorios a la oficina.</summary>
    private const int OfficeAlertsVersion = 38;

    // Misma cáscara que las del Wallet; solo cambia el rótulo de arriba.
    private static string AlertHeader(string eyebrow, string accent, string title) =>
        $"""<tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">{eyebrow}</td></tr>"""
        + $"""<tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="{accent}" style="background-color:{accent};height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>"""
        + $"""<tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;color:#23384B;mso-line-height-rule:exactly;">{title}</td></tr>""";

    // Fila que solo aparece si llega su variable.
    private static string OptionalRow(string variable, string label) =>
        $"{{% if {variable} %}}" + WalletRow(label, "{{ " + variable + " }}") + "{% endif %}";

    private static (string, VariableType, bool, string?, string?) ActionUrl(string description) =>
        ("action_url", VariableType.Url, true, null, description);

    private const string PaymentsAudienceNote =
        "You're receiving this because you manage your office's online payments on {{ product_name }}.";

    private const string StorageAudienceNote =
        "You're receiving this because you manage your office's storage on {{ product_name }}.";

    private const string LegalAudienceNote =
        "You're receiving this because you handle legal matters for your office's files on {{ product_name }}.";

    private const string OnlinePaymentsUrl = "Settings → Online payments en el CRM (/settings/billing).";
    private const string FileUrl = "El archivo en Documents del CRM (/documents?fileId={id}).";

    private static NotificationTemplateSeed PayoutFailed { get; } =
        new(
            EventKey: "payments.payout_failed.v1",
            TemplateKey: "payments.payout_failed.v1",
            Name: "Payments — Payout failed",
            Subject: "Action needed: a payout of {{ amount_label }} failed",
            Html: WalletShell(
                AlertHeader("Online payments", "#DC2626", "A payout failed"),
                WalletParagraph(
                    "We couldn't send <strong style=\"color:#23384B;\">{{ amount_label }}</strong> to your office's bank account."
                ),
                WalletDetails(
                    WalletRow("Amount", "{{ amount_label }}"),
                    WalletRow("Date", "{{ failed_at }}"),
                    OptionalRow("failure_reason", "Reason"),
                    OptionalRow("payout_reference", "Reference")
                ),
                WalletParagraph("Check the bank account connected to your payment provider and update it if needed."),
                WalletButton("{{ action_url }}", "Review online payments"),
                WalletNote(PaymentsAudienceNote)
            ),
            Variables:
            [
                ("amount_label", VariableType.String, true, null, "Monto ya formateado (120.00 USD)."),
                ("failed_at", VariableType.Date, true, null, "Instante tipado del fallo (FailedAtUtc)."),
                ("failure_reason", VariableType.String, false, null, "Motivo que dio el proveedor (FailureReason)."),
                (
                    "payout_reference",
                    VariableType.String,
                    false,
                    null,
                    "Referencia del depósito en el proveedor (ProviderPayoutReference)."
                ),
                ActionUrl(OnlinePaymentsUrl),
                ProductName,
            ],
            ContentVersion: OfficeAlertsVersion
        );

    private static NotificationTemplateSeed ConnectAccountRestricted { get; } =
        new(
            EventKey: "payments.connect_account_restricted.v1",
            TemplateKey: "payments.connect_account_restricted.v1",
            Name: "Payments — Payments account restricted",
            Subject: "Action needed: your payments account is restricted",
            Html: WalletShell(
                AlertHeader("Online payments", "#DC2626", "Your payments account is restricted"),
                WalletParagraph(
                    "Your payment provider paused online payments for your office. Clients can't pay online until you provide the information it asks for."
                ),
                WalletDetails(
                    WalletRow("Date", "{{ restricted_at }}"),
                    OptionalRow("requirements_count", "Pending items")
                ),
                WalletButton("{{ action_url }}", "Review online payments"),
                WalletNote(PaymentsAudienceNote)
            ),
            Variables:
            [
                ("restricted_at", VariableType.Date, true, null, "Instante tipado de la restricción (OccurredOn)."),
                (
                    "requirements_count",
                    VariableType.Number,
                    false,
                    null,
                    "Requisitos pendientes (RequirementsCurrentlyDue.Count); omitir si es 0."
                ),
                ActionUrl(OnlinePaymentsUrl),
                ProductName,
            ],
            ContentVersion: OfficeAlertsVersion
        );

    private static NotificationTemplateSeed ConnectAccountOnboardingRequired { get; } =
        new(
            EventKey: "payments.connect_account_onboarding_required.v1",
            TemplateKey: "payments.connect_account_onboarding_required.v1",
            Name: "Payments — Finish payments account setup",
            Subject: "Finish setting up your payments account",
            Html: WalletShell(
                AlertHeader("Online payments", "#D97706", "Finish setting up your payments account"),
                WalletParagraph(
                    "Your payment provider needs a few more details before your office can accept online payments."
                ),
                "{% if requirements_count %}"
                    + WalletDetails(WalletRow("Pending items", "{{ requirements_count }}"))
                    + "{% endif %}",
                WalletButton("{{ action_url }}", "Continue setup"),
                WalletNote(PaymentsAudienceNote)
            ),
            Variables:
            [
                (
                    "requirements_count",
                    VariableType.Number,
                    false,
                    null,
                    "Requisitos pendientes (RequirementsCurrentlyDue.Count); omitir si es 0."
                ),
                ActionUrl(OnlinePaymentsUrl),
                ProductName,
            ],
            ContentVersion: OfficeAlertsVersion
        );

    private static NotificationTemplateSeed FileInfected { get; } =
        new(
            EventKey: "storage.file_infected.v1",
            TemplateKey: "storage.file_infected.v1",
            Name: "Storage — File with malware blocked",
            Subject: "Security alert: a file with malware was blocked",
            Html: WalletShell(
                AlertHeader("Storage", "#DC2626", "A file with malware was blocked"),
                WalletParagraph(
                    "Our virus scan found malware in a file uploaded to your office. We moved it to quarantine, so no one can open or download it."
                ),
                WalletDetails(WalletRow("Date", "{{ detected_at }}"), OptionalRow("scan_result", "Scan result")),
                WalletParagraph(
                    "The rest of your files stay available. If you know who uploaded it, let them know and ask for a clean copy."
                ),
                WalletButton("{{ action_url }}", "Review the file"),
                WalletNote(StorageAudienceNote)
            ),
            Variables:
            [
                ("detected_at", VariableType.Date, true, null, "Instante tipado de la detección (OccurredOn)."),
                ("scan_result", VariableType.String, false, null, "Resultado del antivirus (ScanReport)."),
                ActionUrl(FileUrl),
                ProductName,
            ],
            ContentVersion: OfficeAlertsVersion
        );

    private static NotificationTemplateSeed DmcaTakedown { get; } =
        new(
            EventKey: "storage.dmca_takedown.v1",
            TemplateKey: "storage.dmca_takedown.v1",
            Name: "Storage — File blocked by a DMCA notice",
            Subject: "Urgent: a file was blocked by a DMCA notice",
            Html: WalletShell(
                AlertHeader("Legal", "#DC2626", "A file was blocked by a DMCA notice"),
                WalletParagraph(
                    "A file in your office was blocked after a copyright (DMCA) notice. It stays in storage, but no one can open, download or share it."
                ),
                WalletDetails(
                    WalletRow("Date", "{{ blocked_at }}"),
                    OptionalRow("notice_reference", "Notice reference")
                ),
                WalletParagraph("Review the notice. If you believe it's a mistake, you can submit a counter-notice."),
                WalletButton("{{ action_url }}", "Review the file"),
                WalletNote(LegalAudienceNote)
            ),
            Variables:
            [
                ("blocked_at", VariableType.Date, true, null, "Instante tipado del bloqueo (OccurredOn)."),
                ("notice_reference", VariableType.String, false, null, "Id del expediente DMCA (DmcaNoticeId)."),
                ActionUrl(FileUrl),
                ProductName,
            ],
            ContentVersion: OfficeAlertsVersion
        );

    /// <summary>v40 (por plantilla): contranotificación DMCA (N8-01); mismos datos que el takedown.</summary>
    private const int DmcaCounterNoticeVersion = 40;

    private static NotificationTemplateSeed DmcaCounterNotice { get; } =
        new(
            EventKey: "storage.dmca_counter_notice.v1",
            TemplateKey: "storage.dmca_counter_notice.v1",
            Name: "Storage — DMCA counter-notice submitted",
            Subject: "A counter-notice was submitted for a blocked file",
            Html: WalletShell(
                AlertHeader("Legal", "#D97706", "A DMCA counter-notice was submitted"),
                WalletParagraph(
                    "Someone disputed the DMCA notice that blocked a file in your office. The file stays blocked until it's restored."
                ),
                WalletDetails(
                    WalletRow("Date", "{{ submitted_at }}"),
                    OptionalRow("notice_reference", "Notice reference")
                ),
                WalletParagraph("Review the counter-notice and decide whether to restore the file."),
                WalletButton("{{ action_url }}", "Review the file"),
                WalletNote(LegalAudienceNote)
            ),
            Variables:
            [
                (
                    "submitted_at",
                    VariableType.Date,
                    true,
                    null,
                    "Instante tipado de la contranotificación (OccurredOn)."
                ),
                ("notice_reference", VariableType.String, false, null, "Id del expediente DMCA (DmcaNoticeId)."),
                ActionUrl(FileUrl),
                ProductName,
            ],
            ContentVersion: DmcaCounterNoticeVersion
        );

    private static NotificationTemplateSeed DmcaReinstated { get; } =
        new(
            EventKey: "storage.dmca_reinstated.v1",
            TemplateKey: "storage.dmca_reinstated.v1",
            Name: "Storage — File restored after a DMCA notice",
            Subject: "A file blocked by a DMCA notice was restored",
            Html: WalletShell(
                AlertHeader("Legal", "#16A34A", "File restored"),
                WalletParagraph(
                    "A file blocked by a DMCA notice was restored. Your team can open, download and share it again."
                ),
                WalletDetails(
                    WalletRow("Date", "{{ reinstated_at }}"),
                    OptionalRow("notice_reference", "Notice reference")
                ),
                WalletButton("{{ action_url }}", "Open the file"),
                WalletNote(LegalAudienceNote)
            ),
            Variables:
            [
                ("reinstated_at", VariableType.Date, true, null, "Instante tipado de la restauración (OccurredOn)."),
                ("notice_reference", VariableType.String, false, null, "Id del expediente DMCA (DmcaNoticeId)."),
                ActionUrl(FileUrl),
                ProductName,
            ],
            ContentVersion: OfficeAlertsVersion
        );

    private static NotificationTemplateSeed LegalHoldPlaced { get; } =
        new(
            EventKey: "storage.legal_hold_placed.v1",
            TemplateKey: "storage.legal_hold_placed.v1",
            Name: "Storage — File placed on legal hold",
            Subject: "A file was placed on legal hold",
            Html: WalletShell(
                AlertHeader("Legal", "#D97706", "A file was placed on legal hold"),
                WalletParagraph(
                    "A file in your office was placed on legal hold. While the hold is active, the file can't be deleted or expire."
                ),
                WalletDetails(WalletRow("Date", "{{ placed_at }}"), OptionalRow("reason", "Reason")),
                WalletButton("{{ action_url }}", "Review the file"),
                WalletNote(LegalAudienceNote)
            ),
            Variables:
            [
                ("placed_at", VariableType.Date, true, null, "Instante tipado de la retención (OccurredOn)."),
                ("reason", VariableType.String, false, null, "Motivo que escribió quien la puso (Reason)."),
                ActionUrl(FileUrl),
                ProductName,
            ],
            ContentVersion: OfficeAlertsVersion
        );

    private static NotificationTemplateSeed LegalHoldLifted { get; } =
        new(
            EventKey: "storage.legal_hold_lifted.v1",
            TemplateKey: "storage.legal_hold_lifted.v1",
            Name: "Storage — Legal hold lifted",
            Subject: "The legal hold on a file was lifted",
            Html: WalletShell(
                AlertHeader("Legal", "#16A34A", "Legal hold lifted"),
                WalletParagraph(
                    "The legal hold on a file in your office was lifted. The file follows your office's usual retention rules again, so it can be deleted or expire."
                ),
                WalletDetails(WalletRow("Date", "{{ lifted_at }}")),
                WalletButton("{{ action_url }}", "Open the file"),
                WalletNote(LegalAudienceNote)
            ),
            Variables:
            [
                ("lifted_at", VariableType.Date, true, null, "Instante tipado del levantamiento (OccurredOn)."),
                ActionUrl(FileUrl),
                ProductName,
            ],
            ContentVersion: OfficeAlertsVersion
        );
}
