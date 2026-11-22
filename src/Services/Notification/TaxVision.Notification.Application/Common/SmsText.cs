using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Common;

/// <summary>Texto del SMS en el idioma del destinatario (sección <c>sms</c> del archivo de textos).</summary>
public static class SmsText
{
    public const string MfaCode = "auth.mfa_code";
    public const string SignatureInvited = "signature.invited";
    public const string SignatureReminder = "signature.reminder";
    public const string SignatureCertificateReady = "signature.certificate_ready";
    public const string SignatureCertificateReadyForDocument = "signature.certificate_ready.document";
    public const string SignatureReadyForDownload = "signature.ready_for_download";
    public const string SignatureDeliveryReady = "signature.delivery_ready";
    public const string SignatureDeliveryReadyPartial = "signature.delivery_ready.partial";

    public static string For(string key, RecipientLocale locale, IReadOnlyDictionary<string, string> parameters) =>
        For(key, locale.Language, parameters);

    /// <summary>Con el idioma tal como llega en el evento (<c>Es</c>/<c>En</c>); lo desconocido cae a inglés.</summary>
    public static string For(string key, string? language, IReadOnlyDictionary<string, string> parameters) =>
        NotificationCopyCatalog.RenderSms(key, language, parameters)
        ?? throw new KeyNotFoundException($"No SMS copy for '{key}'.");
}
