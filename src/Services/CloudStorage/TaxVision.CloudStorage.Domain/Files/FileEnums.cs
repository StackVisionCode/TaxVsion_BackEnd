namespace TaxVision.CloudStorage.Domain.Files;

public enum FileStatus
{
    PendingUpload,
    PendingScan,
    Scanning,
    Available,
    Infected,
    ScanFailed,
    SoftDeleted,

    // Content moderation (no antivirus): IContentScanner.Verdict == PolicyViolation.
    BlockedByPolicy,

    // Content moderation: IContentScanner.Verdict == Uncertain — requiere revision
    // humana antes de decidir Available vs BlockedByPolicy. Sin flujo de reviewer
    // en este MVP (NoOpContentScanner nunca devuelve Uncertain); el estado existe
    // para que un scanner real no necesite una migracion nueva al enchufarse.
    PendingReview,
}

public enum OwnerType
{
    Tenant,
    Customer,
    User,
    Signature,
    Invoice,
    Communication,
}

public enum FolderType
{
    Documents,
    Receipts,
    Invoices,
    EmailIncoming,
    EmailOutgoing,
    Tasks,
    Signatures,
    Avatars,
    Imports,
    Recordings,

    /// <summary>
    /// Transcripts .txt generados por CommunicationTranscriptWorker (whisper.cpp)
    /// a partir de una grabacion en Recordings. Folder aparte porque
    /// RecordingsPolicy() solo permite .webm/.mp4 — subir un .txt ahi lo
    /// rechazaba siempre por whitelist (UnsupportedType), sin importar que el
    /// archivo fuera legitimo.
    /// </summary>
    Transcripts,
    Backups,

    /// <summary>
    /// HTML/text/design-JSON/preview-PNG de EmailTemplateVersion y EmailLayoutVersion (Scribe Fase 5).
    /// Sin RequiresYear: no son documentos fiscales, son artefactos de contenido versionados por
    /// TemplateKey/VersionNumber en la BD de Scribe, no por año.
    /// </summary>
    Templates,

    /// <summary>
    /// Logo del tenant embebido por Postmaster como inline attachment CID en cada correo saliente
    /// (Tenant_Service_LogoSupport_Plan.md). Sin RequiresYear — no es un documento fiscal, se
    /// reemplaza in-place (un logo activo por tenant).
    /// </summary>
    Branding,
    Other,
}

public static class FolderTypeRules
{
    public static bool RequiresYear(this FolderType type) =>
        type
            is FolderType.Documents
                or FolderType.Receipts
                or FolderType.Invoices
                or FolderType.EmailIncoming
                or FolderType.EmailOutgoing
                or FolderType.Tasks
                or FolderType.Signatures;

    public static string ToSegment(this FolderType type) =>
        type switch
        {
            FolderType.EmailIncoming => "email-incoming",
            FolderType.EmailOutgoing => "email-outgoing",
            _ => type.ToString().ToLowerInvariant(),
        };

    public static string ToSegment(this OwnerType type) => type.ToString().ToLowerInvariant();
}
