namespace TaxVision.Notification.Infrastructure.Push;

/// <summary>
/// Credenciales de la cuenta de servicio de Firebase (FCM HTTP v1 API). Mismo criterio que
/// <c>CmsSignerOptions</c> de Signature para el PFX de sellado: en dev/staging apunta a un
/// JSON descargado de Firebase Console; en producción se monta como secreto de archivo
/// (nunca embebido en appsettings ni en variables de entorno en texto plano).
/// </summary>
public sealed class FcmOptions
{
    public const string SectionName = "Notification:Push:Fcm";

    public string ServiceAccountJsonPath { get; set; } = string.Empty;
}
