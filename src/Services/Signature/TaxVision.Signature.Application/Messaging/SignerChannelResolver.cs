using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Messaging;

/// <summary>
/// Deriva el canal de entrega preferido de un firmante ("Sms" | "Email") para poblar los eventos
/// de integración (invitación, entrega del documento firmado/certificado) que consume Notification.
///
/// <para>
/// Regla: si el firmante requiere verificación por SMS/WhatsApp Y tiene teléfono, prefiere SMS;
/// en cualquier otro caso, Email (el default retro-compatible del contrato). Nunca devuelve "Sms"
/// sin teléfono — el guard evita emitir un canal SMS que Notification no podría entregar.
/// </para>
/// </summary>
public static class SignerChannelResolver
{
    public const string Email = "Email";
    public const string Sms = "Sms";

    public static string PreferredChannelFor(Signer signer)
    {
        ArgumentNullException.ThrowIfNull(signer);

        var prefersSms =
            signer.RequiredVerificationMethod
            is SignerVerificationMethod.SmsOtp
                or SignerVerificationMethod.WhatsAppOtp;
        return prefersSms && signer.PhoneNumber is not null ? Sms : Email;
    }
}
