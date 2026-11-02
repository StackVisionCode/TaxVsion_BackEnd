namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

// Códigos de resultado que Tasks interpreta para un fallo de creación de firma (P-18).
public static class SignatureRequestFromFormOutcomes
{
    public const string ClientFormsSourceService = "tasks.client_forms";
    public const string ClientFormRevisionSourceType = "ClientFormRevision";

    public const string HashMismatch = "Signature.HashMismatch";
    public const string FileNotAvailable = "Signature.FileNotAvailable";
    public const string OwnerMismatch = "Signature.OwnerMismatch";
    public const string InvalidRequest = "Signature.InvalidRequest";

    // La copia existe y casa el hash, pero el servidor la rechazó como documento a firmar (no es un PDF
    // válido, supera el tamaño o las páginas del tenant, o ya trae firmas).
    public const string DocumentRejected = "Signature.DocumentRejected";

    // Cliente registrado sin email resoluble en la proyección de Signature (p. ej. aún no proyectado).
    public const string SignerEmailUnavailable = "Signature.SignerEmailUnavailable";
}
