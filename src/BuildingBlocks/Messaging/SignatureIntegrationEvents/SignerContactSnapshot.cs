namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>
/// Copia de los datos de contacto de un firmante al momento de publicar el evento —
/// evita que un consumer (p. ej. Notification) tenga que resolver el firmante contra
/// Signature de forma síncrona solo para poder enviar un correo.
/// </summary>
public sealed record SignerContactSnapshot(Guid SignerId, string Email, string FullName, string Language);
