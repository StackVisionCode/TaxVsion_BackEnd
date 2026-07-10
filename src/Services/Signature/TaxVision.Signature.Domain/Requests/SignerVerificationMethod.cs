namespace TaxVision.Signature.Domain.Requests;

/// <summary>
/// Método concreto de verificación adicional del firmante. Se usa como discriminador de:
/// <list type="bullet">
///   <item>El challenge activo en el <see cref="Signer"/> (para OTPs con expiración).</item>
///   <item>El evento genérico <c>SignerVerificationChallengeIssuedIntegrationEvent</c>
///     que consumen los microservicios entregadores (SMS gateway, Email, etc.).</item>
/// </list>
///
/// <para>
/// La lista es extensible: cuando aparezca un nuevo canal (WhatsApp, Auth app push,
/// KBA quiz), se agrega aquí sin refactor. Los servicios entregadores externos filtran
/// por este valor.
/// </para>
/// </summary>
public enum SignerVerificationMethod
{
    /// <summary>PIN configurado por el staff (Practitioner PIN — Form 8879).</summary>
    PractitionerPin,

    /// <summary>Código OTP entregado por SMS al teléfono del firmante.</summary>
    SmsOtp,

    /// <summary>Código OTP entregado por email al firmante.</summary>
    EmailOtp,

    /// <summary>Código OTP entregado por WhatsApp Business API.</summary>
    WhatsAppOtp,

    /// <summary>Preguntas Knowledge-Based Authentication (LexisNexis, Experian).</summary>
    KbaQuiz,
}
