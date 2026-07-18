namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>
/// Contrato genérico de "reto de verificación emitido a un firmante". Signature lo emite
/// cada vez que un firmante inicia un canal que exige entrega (SMS, WhatsApp, Email OTP,
/// KBA quiz). Los microservicios entregadores (SMS gateway, Email service, KBA provider)
/// filtran por <c>Method</c> y entregan el valor en claro al firmante por su canal.
///
/// <para>
/// Contrato de extensibilidad: cuando aparezca un microservicio nuevo (p.ej.
/// <c>SmsGateway</c> con Twilio) sólo debe:
/// </para>
/// <list type="number">
///   <item>Suscribirse a este evento.</item>
///   <item>Filtrar por el <see cref="Method"/> que le compete (ej. <c>SmsOtp</c>).</item>
///   <item>Entregar <see cref="PlaintextAnswer"/> al firmante via <see cref="DeliveryAddress"/>.</item>
/// </list>
/// <para>
/// No debe modificar Signature. Signature no conoce a Twilio, Vonage, ni al servicio
/// concreto — es un fan-out.
/// </para>
///
/// <para>
/// Consideraciones de seguridad:
/// </para>
/// <list type="bullet">
///   <item><see cref="PlaintextAnswer"/> es sensible. Los consumers no deben loggearlo
///     por encima de <c>Debug</c>.</item>
///   <item>El bus es interno; el evento nunca cruza la frontera pública.</item>
///   <item>Signature sólo guarda el hash del reto — el valor en claro sólo vive en este
///     evento el tiempo que tarda el consumer en entregarlo.</item>
/// </list>
/// </summary>
public sealed record SignerVerificationChallengeIssuedIntegrationEvent : IntegrationEvent
{
    public required Guid SignatureRequestId { get; init; }
    public required Guid SignerId { get; init; }

    /// <summary>Nombre del método (SmsOtp | EmailOtp | WhatsAppOtp | KbaQuiz | ...).</summary>
    public required string Method { get; init; }

    /// <summary>
    /// Destinatario en el canal correspondiente (número E.164, email, etc.). Formato
    /// depende de <see cref="Method"/> — el consumer sabe interpretarlo.
    /// </summary>
    public required string DeliveryAddress { get; init; }

    /// <summary>El valor en claro que el consumer debe entregar. Sensible; no loggear.</summary>
    public required string PlaintextAnswer { get; init; }

    public required string SignerFullName { get; init; }
    public required string SignerLanguage { get; init; }
    public required DateTime ExpiresAtUtc { get; init; }
}
