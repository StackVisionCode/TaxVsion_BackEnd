using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Tenancy;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Application.Terms.Versions;

/// <summary>
/// Cómo se guarda en CloudStorage el documento legal (ToS/Privacy): recurso de plataforma bajo
/// <see cref="PlatformTenant.Id"/>, dueño Tenant sin id y carpeta Templates.
/// </summary>
public static class TermsContentFile
{
    public const string RequestingService = "auth";
    public const string OwnerType = "Tenant";
    public const string FolderType = "Templates";

    /// <summary>Pedido de catálogo a CloudStorage del documento ya subido al bucket temporal.</summary>
    public static SaveFileRequestedIntegrationEvent SaveFileRequested(
        StoredTermsContent stored,
        string fileName,
        string contentType,
        long sizeBytes,
        Guid actorId,
        string correlationId
    ) =>
        new()
        {
            TenantId = PlatformTenant.Id,
            CorrelationId = correlationId,
            FileId = stored.FileId,
            RequestingService = RequestingService,
            SourceBucket = stored.SourceBucket,
            SourceObjectKey = stored.SourceObjectKey,
            ActorId = actorId,
            OwnerType = OwnerType,
            OwnerId = null,
            FolderType = FolderType,
            TaxYear = null,
            OriginalName = fileName,
            ContentType = contentType,
            SizeBytes = sizeBytes,
        };

    /// <summary>¿El evento de CloudStorage habla de un documento legal pedido por Auth?</summary>
    public static bool IsTermsContent(
        string? requestingService,
        Guid tenantId,
        string? ownerType,
        string? folderType
    ) =>
        string.Equals(requestingService, RequestingService, StringComparison.Ordinal)
        && tenantId == PlatformTenant.Id
        && string.Equals(ownerType, OwnerType, StringComparison.OrdinalIgnoreCase)
        && string.Equals(folderType, FolderType, StringComparison.OrdinalIgnoreCase);
}
