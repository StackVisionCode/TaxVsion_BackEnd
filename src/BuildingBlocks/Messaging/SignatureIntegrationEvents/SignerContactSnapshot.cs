namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>
/// Copia de los datos de contacto de un firmante al momento de publicar el evento —
/// evita que un consumer (p. ej. Notification) tenga que resolver el firmante contra
/// Signature de forma síncrona solo para poder enviar un correo.
/// </summary>
/// <param name="MappedCustomerId">
/// Fase 6 del plan de notificaciones dinámicas: si el firmante es un cliente registrado
/// del tenant (no externo), el CustomerId al que está vinculado — permite a un consumer
/// resolver si ese cliente tiene cuenta de portal activa, sin llamar a Signature.
/// </param>
public sealed record SignerContactSnapshot(
    Guid SignerId,
    string Email,
    string FullName,
    string Language,
    Guid? MappedCustomerId = null
);
