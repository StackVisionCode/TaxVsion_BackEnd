namespace BuildingBlocks.Messaging.CloudStorageIntegrationEvents;

public sealed record FileAvailableIntegrationEvent : IntegrationEvent
{
    public required Guid FileId { get; init; }
    public required string ObjectKey { get; init; }
    public required string ContentType { get; init; }
    public required long SizeBytes { get; init; }
    public required string ChecksumSha256 { get; init; }
}

public sealed record FileInfectedDetectedIntegrationEvent : IntegrationEvent
{
    public required Guid FileId { get; init; }
    public required string ObjectKey { get; init; }
    public required string ScanReport { get; init; }
}

/// <summary>
/// Publicado por IContentScanner (moderacion de contenido, no antivirus)
/// cuando el verdict es PolicyViolation. Objeto ya movido a QuarantineBucket.
/// </summary>
public sealed record FileBlockedByPolicyIntegrationEvent : IntegrationEvent
{
    public required Guid FileId { get; init; }
    public required string ObjectKey { get; init; }
    public required string PolicyReason { get; init; }
}

/// <summary>
/// Publicado cuando IContentScanner devuelve Uncertain — requiere revision
/// humana. No hay flujo de reviewer en el MVP (NoOpContentScanner nunca lo
/// dispara); el evento existe para que Notification pueda alertar a
/// compliance en cuanto exista un scanner real.
/// </summary>
public sealed record FilePendingReviewIntegrationEvent : IntegrationEvent
{
    public required Guid FileId { get; init; }
    public required string ObjectKey { get; init; }
    public required string Reason { get; init; }
}

public sealed record FileDeletedIntegrationEvent : IntegrationEvent
{
    public required Guid FileId { get; init; }
}

/// <summary>
/// Publicado al restaurar un archivo desde la papelera (Fase C1) — contraparte de
/// FileDeletedIntegrationEvent. Sin consumer todavia; existe para que servicios
/// que reaccionaron al borrado (ej. Communication marcando un adjunto como
/// eliminado en el chat) puedan revertir ese estado cuando se conecte uno.
/// </summary>
public sealed record FileRestoredIntegrationEvent : IntegrationEvent
{
    public required Guid FileId { get; init; }
}

public sealed record StorageLimitExceededIntegrationEvent : IntegrationEvent
{
    public required long AttemptedFileSizeBytes { get; init; }
    public required long UsedBytes { get; init; }
    public required long ReservedBytes { get; init; }
    public required long MaxBytes { get; init; }
}

public sealed record FileAccessAuditedIntegrationEvent : IntegrationEvent
{
    public required Guid FileId { get; init; }
    public required Guid ActorId { get; init; }
    public required string Action { get; init; }
    public required string Outcome { get; init; }
    public string? IpAddress { get; init; }
}

/// <summary>Fase C3/C4 — publicado al crear un link de compartir (archivo o carpeta).</summary>
public sealed record ShareLinkCreatedIntegrationEvent : IntegrationEvent
{
    public required Guid ShareLinkId { get; init; }
    public required Guid ResourceId { get; init; }
    public required string ResourceType { get; init; }
    public required string Visibility { get; init; }
    public required Guid CreatedByUserId { get; init; }
}

public sealed record ShareLinkRevokedIntegrationEvent : IntegrationEvent
{
    public required Guid ShareLinkId { get; init; }
    public required Guid ResourceId { get; init; }
    public required string ResourceType { get; init; }
}

/// <summary>
/// Fase C4 — publicado cuando un archivo se agrega (por move) a una carpeta que
/// ya tiene un ShareLink Public activo (directo o heredado de un ancestro
/// recursivo). AutoCovered indica si AppliesToFutureItems ya lo deja accesible
/// por ese link, o si solo es un aviso porque el link no cubre items nuevos.
/// </summary>
public sealed record ShareLinkFolderItemAddedIntegrationEvent : IntegrationEvent
{
    public required Guid ShareLinkId { get; init; }
    public required Guid FolderId { get; init; }
    public required Guid FileId { get; init; }
    public required bool AutoCovered { get; init; }
}

/// <summary>Publicado cada vez que un link se resuelve exitosamente (publico o privado) y sirve una presigned URL.</summary>
public sealed record ShareLinkAccessedIntegrationEvent : IntegrationEvent
{
    public required Guid ShareLinkId { get; init; }
    public required Guid FileId { get; init; }
    public required string Channel { get; init; }
}

/// <summary>
/// Publicado en cualquier intento de resolucion denegado (token invalido, revocado,
/// agotado, expirado, tenant/identidad no autorizados, etc). Deliberadamente NO
/// distingue el motivo exacto en un campo estructurado mas alla de Reason (texto
/// libre para logging/alertas) — el endpoint sigue devolviendo el mismo 404
/// generico al cliente (anti-enumeracion, ver ShareAccessResult.Denied); esto es
/// solo para que un consumidor externo (SIEM/alertas) pueda reaccionar.
/// </summary>
public sealed record ShareLinkAccessDeniedIntegrationEvent : IntegrationEvent
{
    public required string Channel { get; init; }
    public required string Reason { get; init; }
    public Guid? ShareLinkId { get; init; }
}

/// <summary>
/// Publicado cuando una resolucion descubre que el link ya vencio (ExpiresAtUtc
/// paso). Subconjunto de ShareLinkAccessDenied con semantica mas especifica — la
/// expiracion se sigue evaluando siempre en vivo (ver ShareLink.IsExpired), no hay
/// un job que la detecte proactivamente; este evento se dispara en el momento en
/// que un acceso real la encuentra.
/// </summary>
public sealed record ShareLinkExpiredIntegrationEvent : IntegrationEvent
{
    public required Guid ShareLinkId { get; init; }
    public required Guid ResourceId { get; init; }
    public required DateTime ExpiresAtUtc { get; init; }
}

/// <summary>Fase C4 (completitud) — publicado al cambiar el Permission de un link ya creado.</summary>
public sealed record ShareLinkPermissionChangedIntegrationEvent : IntegrationEvent
{
    public required Guid ShareLinkId { get; init; }
    public required Guid ResourceId { get; init; }
    public required string OldPermission { get; init; }
    public required string NewPermission { get; init; }
}

/// <summary>
/// Fase L1.3 — publicado cuando el equipo legal registra un takedown DMCA.
/// A diferencia de FileBlockedByPolicyIntegrationEvent, el objeto NO se mueve a
/// QuarantineBucket (sigue en MainBucket, solo se bloquea el acceso a nivel de
/// aplicacion) — evento propio para no mentirle a los consumidores sobre eso.
/// </summary>
public sealed record FileBlockedByDmcaTakedownIntegrationEvent : IntegrationEvent
{
    public required Guid FileId { get; init; }
    public required Guid DmcaNoticeId { get; init; }
}

/// <summary>Fase L1.3 — publicado cuando el equipo legal reinstala un archivo tras resolver un expediente DMCA.</summary>
public sealed record FileReinstatedFromTakedownIntegrationEvent : IntegrationEvent
{
    public required Guid FileId { get; init; }
    public required Guid DmcaNoticeId { get; init; }
}

/// <summary>
/// Fase D0 — reemplaza el patron HTTP+M2M (initiate/PUT/complete) para servicios de
/// negocio que ya subieron el objeto directo a MinIO con credenciales propias (IAM
/// scoped a su prefijo dentro de TempBucket, ej. taxvision-temp/signature/*). El
/// FileId lo genera el SERVICIO LLAMADOR, no CloudStorage: sin eso el llamador no
/// podria referenciar el archivo en su propio aggregate antes de que el scan
/// asincrono termine (ver SignatureRequestCompletedConsumer en Signature, que marca
/// SealedFileId en el mismo instante que sube el archivo). Ese mismo FileId sirve de
/// idempotencia — no hace falta un IdempotencyKey aparte: un redelivery con el mismo
/// FileId choca contra la unique constraint (Id/ObjectKey) y SaveFileFromSourceHandler
/// lo trata como no-op.
/// </summary>
public sealed record SaveFileRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid FileId { get; init; }

    /// <summary>Nombre corto del servicio llamador (ej. "signature") — solo para auditoria/logs, no gobierna autorizacion (eso lo hace el IAM de MinIO).</summary>
    public required string RequestingService { get; init; }
    public required string SourceBucket { get; init; }
    public required string SourceObjectKey { get; init; }
    public required Guid ActorId { get; init; }

    /// <summary>Debe matchear el enum OwnerType de CloudStorage.Domain (no referenciable desde BuildingBlocks) — mismo criterio que SignaturePdfUpload.</summary>
    public required string OwnerType { get; init; }
    public Guid? OwnerId { get; init; }

    /// <summary>Debe matchear el enum FolderType de CloudStorage.Domain.</summary>
    public required string FolderType { get; init; }
    public int? TaxYear { get; init; }
    public required string OriginalName { get; init; }
    public required string ContentType { get; init; }
    public required long SizeBytes { get; init; }
}
