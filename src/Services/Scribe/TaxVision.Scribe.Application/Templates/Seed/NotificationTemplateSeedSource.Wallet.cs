using TaxVision.Scribe.Domain.Templates;

namespace TaxVision.Scribe.Application.Templates.Seed;

/// <summary>
/// Correos del Wallet a quien tiene <c>wallet.manage</c>: saldo bajo y recarga automática (recibo, pausa y tope
/// diario). Los montos llegan ya formateados; la fecha del recibo llega tipada y Scribe la imprime en la zona de
/// la oficina. Los manda la plataforma, sobre <c>system-base</c>.
/// </summary>
public static partial class NotificationTemplateSeedSource
{
    /// <summary>v36 (por plantilla): las 4 plantillas nuevas del Wallet.</summary>
    private const int WalletAlertsVersion = 36;

    // ---------- Piezas de la cáscara interna (mismos estilos que las plantillas de facturación) ----------

    private static string WalletHeader(string accent, string title) =>
        """<tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Wallet</td></tr>"""
        + $"""<tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="{accent}" style="background-color:{accent};height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>"""
        + $"""<tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;color:#23384B;mso-line-height-rule:exactly;">{title}</td></tr>""";

    private static string WalletParagraph(string text) =>
        $"""<tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">{text}</td></tr>""";

    private static string WalletRow(string label, string value) =>
        $"""<tr><td style="padding:6px 0;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#70869A;">{label}</td><td align="right" style="padding:6px 0;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;font-weight:bold;color:#23384B;">{value}</td></tr>""";

    private static string WalletDetails(params string[] rows) =>
        """<tr><td style="padding-bottom:12px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#F4F8FB" style="background-color:#F4F8FB;border-radius:10px;"><tr><td style="padding:10px 18px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">"""
        + string.Concat(rows)
        + "</table></td></tr></table></td></tr>";

    private static string WalletButton(string url, string label) =>
        $"""<tr><td align="left" style="padding:14px 0 18px 0;"><table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{url}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">{label}</a></td></tr></table></td></tr>""";

    private static string WalletNote(string text) =>
        $"""<tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">{text}</td></tr>""";

    private static string WalletShell(params string[] rows) =>
        """<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">"""
        + string.Concat(rows)
        + "</table>";

    private const string WalletAudienceNote =
        "You're receiving this because you can manage your office's Wallet on {{ product_name }}.";

    private static readonly (
        string Name,
        VariableType Type,
        bool Required,
        string? DefaultValue,
        string? Description
    ) WalletProductName = (
        "product_name",
        VariableType.String,
        false,
        "TaxOffix",
        "Branding del producto (lo inyecta el renderer desde PlatformSettings)."
    );

    private static NotificationTemplateSeed WalletLowBalance { get; } =
        new(
            EventKey: "wallet.low_balance.v1",
            TemplateKey: "wallet.low_balance",
            Name: "Wallet — Low balance",
            Subject: "Your wallet balance is low",
            Html: WalletShell(
                WalletHeader("#D97706", "Your wallet balance is low"),
                WalletParagraph(
                    "Your office's Wallet has <strong style=\"color:#23384B;\">{{ available_label }}</strong> left, below your alert threshold of <strong style=\"color:#23384B;\">{{ threshold_label }}</strong>."
                ),
                WalletParagraph("Add funds so text messages and campaigns keep sending without interruption."),
                WalletButton("{{ wallet_url }}", "Add funds"),
                WalletNote(
                    "You can change the alert threshold or turn on automatic recharge in <a href=\"{{ settings_url }}\" target=\"_blank\" style=\"color:#1E466B;text-decoration:underline;\">Settings &rarr; Wallet</a>. "
                        + WalletAudienceNote
                )
            ),
            Variables:
            [
                ("available_label", VariableType.String, true, null, "Saldo disponible ya formateado ($3.20)."),
                ("threshold_label", VariableType.String, true, null, "Umbral de aviso ya formateado ($5.00)."),
                ("wallet_url", VariableType.Url, true, null, "Wallet de la oficina en el CRM."),
                ("settings_url", VariableType.Url, true, null, "Settings → Wallet en el CRM."),
                WalletProductName,
            ],
            ContentVersion: WalletAlertsVersion
        );

    private static NotificationTemplateSeed WalletAutoRechargeSucceeded { get; } =
        new(
            EventKey: "wallet.auto_recharge_succeeded.v1",
            TemplateKey: "wallet.auto_recharge_succeeded",
            Name: "Wallet — Automatic recharge receipt",
            Subject: "Receipt: {{ amount_label }} automatic recharge",
            Html: WalletShell(
                WalletHeader("#16A34A", "Automatic recharge completed"),
                WalletParagraph(
                    "Your Wallet balance went below your threshold, so we added <strong style=\"color:#23384B;\">{{ amount_label }}</strong> using your saved payment method."
                ),
                WalletDetails(
                    WalletRow("Amount", "{{ amount_label }}"),
                    WalletRow("Payment method", "{{ method_label }}"),
                    WalletRow("Balance after recharge", "{{ balance_after_label }}"),
                    WalletRow("Date", "{{ charged_at }}")
                ),
                WalletButton("{{ wallet_url }}", "View your Wallet"),
                WalletNote(
                    "You turned on automatic recharge. You can change it or turn it off any time in <a href=\"{{ settings_url }}\" target=\"_blank\" style=\"color:#1E466B;text-decoration:underline;\">Settings &rarr; Wallet</a>. "
                        + WalletAudienceNote
                )
            ),
            Variables:
            [
                ("amount_label", VariableType.String, true, null, "Monto cobrado ya formateado ($25.00)."),
                ("method_label", VariableType.String, true, null, "Método cobrado (Visa •••• 4242)."),
                ("balance_after_label", VariableType.String, true, null, "Saldo disponible después de la recarga."),
                (
                    "charged_at",
                    VariableType.Date,
                    true,
                    null,
                    "Instante tipado del cobro: Scribe imprime fecha y hora en la zona de la oficina."
                ),
                ("wallet_url", VariableType.Url, true, null, "Wallet de la oficina en el CRM."),
                ("settings_url", VariableType.Url, true, null, "Settings → Wallet en el CRM."),
                WalletProductName,
            ],
            ContentVersion: WalletAlertsVersion
        );

    private static NotificationTemplateSeed WalletAutoRechargePaused { get; } =
        new(
            EventKey: "wallet.auto_recharge_paused.v1",
            TemplateKey: "wallet.auto_recharge_paused",
            Name: "Wallet — Automatic recharge paused",
            Subject: "Automatic recharge is paused",
            Html: WalletShell(
                WalletHeader("#D97706", "Automatic recharge is paused"),
                WalletParagraph("{{ reason_text }}"),
                WalletParagraph(
                    "We paused automatic recharge, and it won't retry on its own. Review your payment method in <strong style=\"color:#23384B;\">Settings &rarr; Wallet</strong> and turn it back on."
                ),
                WalletDetails(
                    WalletRow("Recharge amount", "{{ amount_label }}"),
                    WalletRow("Payment method", "{{ method_label }}")
                ),
                WalletButton("{{ settings_url }}", "Review Wallet settings"),
                WalletNote("Until then, your balance won't be topped up automatically. " + WalletAudienceNote)
            ),
            Variables:
            [
                ("reason_text", VariableType.String, true, null, "Por qué se pausó, ya redactado por Notification."),
                ("amount_label", VariableType.String, true, null, "Monto de la recarga ya formateado ($25.00)."),
                ("method_label", VariableType.String, true, null, "Método guardado (Visa •••• 4242)."),
                ("settings_url", VariableType.Url, true, null, "Settings → Wallet en el CRM."),
                WalletProductName,
            ],
            ContentVersion: WalletAlertsVersion
        );

    private static NotificationTemplateSeed WalletAutoRechargeDailyLimit { get; } =
        new(
            EventKey: "wallet.auto_recharge_daily_limit.v1",
            TemplateKey: "wallet.auto_recharge_daily_limit",
            Name: "Wallet — Automatic recharge daily limit",
            Subject: "Daily automatic recharge limit reached",
            Html: WalletShell(
                WalletHeader("#67BAF4", "Daily automatic recharge limit reached"),
                WalletParagraph(
                    "Your Wallet was recharged {{ daily_limit }} times today ({{ amount_label }} each). That's the most allowed per day, so it won't recharge again today."
                ),
                WalletParagraph(
                    "Automatic recharge isn't paused: it continues tomorrow (your office's time). If you need funds today, add them from your Wallet."
                ),
                WalletButton("{{ settings_url }}", "Review Wallet settings"),
                WalletNote(
                    "Up to {{ daily_limit }} automatic recharges per day is the limit you agreed to. "
                        + WalletAudienceNote
                )
            ),
            Variables:
            [
                ("daily_limit", VariableType.String, true, null, "Tope de recargas automáticas por día (3)."),
                ("amount_label", VariableType.String, true, null, "Monto de cada recarga ya formateado ($25.00)."),
                ("settings_url", VariableType.Url, true, null, "Settings → Wallet en el CRM."),
                WalletProductName,
            ],
            ContentVersion: WalletAlertsVersion
        );
}
