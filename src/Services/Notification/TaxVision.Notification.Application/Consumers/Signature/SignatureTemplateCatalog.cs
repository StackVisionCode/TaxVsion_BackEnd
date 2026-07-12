namespace TaxVision.Notification.Application.Consumers.Signature;

/// <summary>
/// Catálogo interno de templates de Signature, versionados según convención del plan
/// (<c>sig.{purpose}.v{n}</c>). Cada template define subject/html/text bilingüe.
///
/// <para>
/// La versión se bump cuando cambie el copy o layout. Los consumers registran el key
/// en <c>NotificationLog.TemplateKey</c> — auditable per-tenant. Un editor CMS por
/// tenant es evolución futura (Fase avanzada), pero el catálogo estático cubre los
/// arranques.
/// </para>
/// </summary>
public static class SignatureTemplateCatalog
{
    public const string InvitationKey = "sig.invitation.v1";
    public const string ReminderKey = "sig.reminder.v1";
    public const string CompletedKey = "sig.completed.v1";
    public const string ExpiredKey = "sig.expired.v1";
    public const string DeclinedKey = "sig.declined.v1";
    public const string VerificationChallengeKey = "sig.verification-challenge.v1";

    public static SignatureTemplate Invitation(
        bool isSpanish,
        string fullName,
        string publicUrl,
        DateTime expiresAtUtc,
        bool requiresConsent
    ) =>
        isSpanish
            ? new SignatureTemplate(
                Subject: "TaxVision — Solicitud de firma pendiente",
                Html: $"<p>Hola {fullName},</p>"
                    + $"<p>Tienes una solicitud de firma pendiente en TaxVision.{(requiresConsent ? " Se te pedirá aceptar el consent antes de firmar." : string.Empty)}</p>"
                    + $"<p><a href=\"{publicUrl}\">Abrir solicitud de firma</a></p>"
                    + $"<p>El enlace vence el {expiresAtUtc:yyyy-MM-dd HH:mm} UTC.</p>"
                    + "<p>— TaxVision</p>",
                Text: $"Hola {fullName},\n\nTienes una solicitud de firma pendiente. {(requiresConsent ? "Se te pedirá aceptar el consent antes de firmar." : string.Empty)}\n\nAbrir: {publicUrl}\nVence: {expiresAtUtc:yyyy-MM-dd HH:mm} UTC.\n\n— TaxVision"
            )
            : new SignatureTemplate(
                Subject: "TaxVision — Signature request pending",
                Html: $"<p>Hi {fullName},</p>"
                    + $"<p>You have a pending signature request on TaxVision.{(requiresConsent ? " You'll be asked to accept the consent before signing." : string.Empty)}</p>"
                    + $"<p><a href=\"{publicUrl}\">Open signature request</a></p>"
                    + $"<p>The link expires on {expiresAtUtc:yyyy-MM-dd HH:mm} UTC.</p>"
                    + "<p>— TaxVision</p>",
                Text: $"Hi {fullName},\n\nYou have a pending signature request.{(requiresConsent ? " You'll be asked to accept the consent before signing." : string.Empty)}\n\nOpen: {publicUrl}\nExpires: {expiresAtUtc:yyyy-MM-dd HH:mm} UTC.\n\n— TaxVision"
            );

    public static SignatureTemplate Reminder(
        bool isSpanish,
        string fullName,
        string publicUrl,
        DateTime expiresAtUtc,
        int remindersSent
    ) =>
        isSpanish
            ? new SignatureTemplate(
                Subject: "TaxVision — Recordatorio: tu firma sigue pendiente",
                Html: $"<p>Hola {fullName},</p>"
                    + $"<p>Este es un recordatorio ({remindersSent} de 3) de que tu firma sigue pendiente en TaxVision. El enlace vence el {expiresAtUtc:yyyy-MM-dd HH:mm} UTC.</p>"
                    + $"<p><a href=\"{publicUrl}\">Abrir solicitud de firma</a></p>"
                    + "<p>— TaxVision</p>",
                Text: $"Hola {fullName},\n\nRecordatorio ({remindersSent} de 3): tu firma sigue pendiente. Vence: {expiresAtUtc:yyyy-MM-dd HH:mm} UTC.\nAbrir: {publicUrl}\n\n— TaxVision"
            )
            : new SignatureTemplate(
                Subject: "TaxVision — Reminder: your signature is still pending",
                Html: $"<p>Hi {fullName},</p>"
                    + $"<p>This is reminder {remindersSent} of 3 that your signature is still pending on TaxVision. The link expires on {expiresAtUtc:yyyy-MM-dd HH:mm} UTC.</p>"
                    + $"<p><a href=\"{publicUrl}\">Open signature request</a></p>"
                    + "<p>— TaxVision</p>",
                Text: $"Hi {fullName},\n\nReminder {remindersSent} of 3: your signature is still pending. Expires: {expiresAtUtc:yyyy-MM-dd HH:mm} UTC.\nOpen: {publicUrl}\n\n— TaxVision"
            );

    public static SignatureTemplate VerificationChallenge(
        bool isSpanish,
        string fullName,
        string code,
        DateTime expiresAtUtc
    ) =>
        isSpanish
            ? new SignatureTemplate(
                Subject: "TaxVision — Código de verificación",
                Html: $"<p>Hola {fullName},</p><p>Tu código de verificación TaxVision es <strong>{code}</strong>.</p><p>Vence el {expiresAtUtc:yyyy-MM-dd HH:mm} UTC.</p><p>Si no solicitaste este código, ignora este correo.</p>",
                Text: $"Hola {fullName},\n\nCódigo: {code}\nVence: {expiresAtUtc:yyyy-MM-dd HH:mm} UTC."
            )
            : new SignatureTemplate(
                Subject: "TaxVision — Verification code",
                Html: $"<p>Hi {fullName},</p><p>Your TaxVision verification code is <strong>{code}</strong>.</p><p>Expires on {expiresAtUtc:yyyy-MM-dd HH:mm} UTC.</p><p>If you did not request this code, please ignore this email.</p>",
                Text: $"Hi {fullName},\n\nCode: {code}\nExpires: {expiresAtUtc:yyyy-MM-dd HH:mm} UTC."
            );
}

public sealed record SignatureTemplate(string Subject, string Html, string Text);
