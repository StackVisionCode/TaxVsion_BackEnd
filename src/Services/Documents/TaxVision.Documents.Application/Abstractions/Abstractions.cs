using BuildingBlocks.Results;
using TaxVision.Documents.Domain.Branding;
using TaxVision.Documents.Domain.Generations;

namespace TaxVision.Documents.Application.Abstractions;

/// <summary>Perfil de marca del tenant (uno por tenant). Implementación EF con IgnoreQueryFilters +
/// tenantId explícito (funciona dentro del scope de Wolverine del render).</summary>
public interface IDocumentBrandingRepository
{
    Task<DocumentBranding?> GetByTenantAsync(Guid tenantId, CancellationToken ct = default);

    Task AddAsync(DocumentBranding branding, CancellationToken ct = default);
}

/// <summary>Acceso a generaciones documentales del tenant. Implementación EF en Infrastructure
/// (con IgnoreQueryFilters + tenantId explícito para funcionar dentro de consumers/jobs de Wolverine).</summary>
public interface IDocumentGenerationRepository
{
    Task<DocumentGeneration?> GetByIdAsync(Guid tenantId, Guid generationId, CancellationToken ct = default);

    /// <summary>Deduplicación por (TenantId, IdempotencyKey). Devuelve la generación existente si la hubiera.</summary>
    Task<DocumentGeneration?> GetByIdempotencyKeyAsync(Guid tenantId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Correlación del evento CloudStorage FileAvailable → la generación que subió ese FileId.</summary>
    Task<DocumentGeneration?> GetByFileIdAsync(Guid fileId, CancellationToken ct = default);

    Task AddAsync(DocumentGeneration generation, CancellationToken ct = default);
}

/// <summary>Resuelve una plantilla documental (HTML/CSS) por clave+versión y produce el HTML final
/// a partir de los datos. Reutiliza el motor de plantillas del repo (Fluid/Liquid). SCAFFOLD.</summary>
public interface IDocumentTemplateRenderer
{
    Task<Result<string>> RenderHtmlAsync(
        string templateKey,
        int templateVersion,
        Guid tenantId,
        IReadOnlyDictionary<string, object?> data,
        CancellationToken ct = default
    );
}

/// <summary>Convierte HTML imprimible en bytes PDF. Implementación Chromium headless (Playwright)
/// con pool + límite de concurrencia. SCAFFOLD.</summary>
public interface IHtmlToPdfConverter
{
    Task<Result<byte[]>> ConvertAsync(string html, CancellationToken ct = default);
}

/// <summary>Codifica un texto (p.ej. el link de pago con el subdominio del tenant) en un QR y lo
/// devuelve como data-URI PNG embebible (sin red, respeta el CSP de la plantilla). Documents solo
/// codifica la URL que recibe; no la fabrica.</summary>
public interface IQrCodeGenerator
{
    string CreatePngDataUri(string content, int pixelsPerModule = 6);
}

/// <summary>Sube el archivo generado al bucket temporal (IAM MinIO propia) y publica
/// SaveFileRequestedIntegrationEvent para que CloudStorage lo almacene permanentemente. Documents
/// nunca guarda bytes. SCAFFOLD.</summary>
public interface IDocumentStorageClient
{
    Task<Result> RequestSaveAsync(
        Guid tenantId,
        Guid fileId,
        byte[] content,
        string fileName,
        string contentType,
        string ownerType,
        Guid ownerId,
        string folderType,
        int? taxYear,
        Guid actorId,
        string correlationId,
        CancellationToken ct = default
    );
}
