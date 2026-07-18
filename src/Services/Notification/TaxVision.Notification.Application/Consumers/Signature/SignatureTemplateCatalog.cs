namespace TaxVision.Notification.Application.Consumers.Signature;

/// <summary>
/// Claves de templates de Signature (convención <c>sig.{purpose}.v{n}</c>), ahora renderizados en
/// Scribe (Fase 8) — el contenido vive allá; este catálogo solo mantiene el nombre estable usado
/// para <c>EmailDispatchRequest.TemplateKey</c>/<c>NotificationLog.TemplateKey</c> (audit) y como
/// EventKey de mapeo en Scribe.
/// </summary>
public static class SignatureTemplateCatalog
{
    public const string InvitationKey = "sig.invitation.v1";
    public const string ReminderKey = "sig.reminder.v1";
    public const string CompletedKey = "sig.completed.v1";
    public const string ExpiredKey = "sig.expired.v1";
    public const string DeclinedKey = "sig.declined.v1";
    public const string VerificationChallengeKey = "sig.verification-challenge.v1";
}
