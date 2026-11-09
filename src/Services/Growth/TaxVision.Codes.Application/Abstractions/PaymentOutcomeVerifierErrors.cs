namespace TaxVision.Codes.Application.Abstractions;

/// <summary>Códigos de <see cref="IPaymentOutcomeVerifier"/>.</summary>
public static class PaymentOutcomeVerifierErrors
{
    /// <summary>La verificación está apagada: el commit tardío queda en revisión.</summary>
    public const string Disabled = "Codes.PaymentOutcomeVerifier.Disabled";

    /// <summary>Código del verificador anterior; quedan revisiones guardadas con él.</summary>
    public const string NotImplemented = "Codes.PaymentOutcomeVerifier.NotImplemented";

    /// <summary>El dueño del pago dice que no está confirmado (o no coincide): queda en revisión.</summary>
    public const string NotConfirmed = "Codes.PaymentOutcomeVerifier.NotConfirmed";

    /// <summary>No hay forma de verificar esa fuente de pago: queda en revisión.</summary>
    public const string UnsupportedSource = "Codes.PaymentOutcomeVerifier.UnsupportedSource";

    /// <summary>No se pudo preguntar (red, token, 5xx): pasajero, se reintenta.</summary>
    public const string Unavailable = "Codes.PaymentOutcomeVerifier.Unavailable";
}
