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

    /// <summary>Deduplicación por (TenantId, SourceService, IdempotencyKey), la clave del índice único.</summary>
    Task<DocumentGeneration?> GetByIdempotencyKeyAsync(
        Guid tenantId,
        string sourceService,
        string idempotencyKey,
        CancellationToken ct = default
    );

    /// <summary>Correlación del evento CloudStorage FileAvailable → la generación que subió ese FileId.</summary>
    Task<DocumentGeneration?> GetByFileIdAsync(Guid fileId, CancellationToken ct = default);

    /// <summary>Cross-tenant: la generación cuya copia de firma (P-18) es ese FileId.</summary>
    Task<DocumentGeneration?> GetBySignatureCopyFileIdAsync(Guid fileId, CancellationToken ct = default);

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

/// <summary>Cuenta las páginas de un PDF ya renderizado (P-18: ubicar la caja de firma en la última
/// página). Implementación PdfSharp en Infrastructure, solo lee la estructura, no renderiza fuentes.</summary>
public interface IPdfPageCounter
{
    int CountPages(byte[] pdf);
}

/// <summary>Codifica un texto (p.ej. el link de pago con el subdominio del tenant) en un QR y lo
/// devuelve como data-URI PNG embebible (sin red, respeta el CSP de la plantilla). Documents solo
/// codifica la URL que recibe; no la fabrica.</summary>
public interface IQrCodeGenerator
{
    string CreatePngDataUri(string content, int pixelsPerModule = 6);
}

/// <summary>PayFlow (Fase 10) — datos fijos del emisor plataforma (TaxVision Inc.) para el recibo
/// de onboarding: a diferencia de Invoice (branding por tenant), este documento lo emite la
/// plataforma misma, antes de que exista un tenant. Config estática, sin I/O — método síncrono.</summary>
public interface IPlatformIssuerProvider
{
    IssuerSnapshot GetSnapshot();
}

public sealed record IssuerSnapshot(
    string Name,
    string TaxId,
    string AddressLine1,
    string City,
    string State,
    string PostalCode,
    string Country,
    string Phone,
    string Email,
    string Website,
    string? LogoDataUri
);

/// <summary>Objeto ya subido al bucket temporal, listo para pedir su guardado a CloudStorage.</summary>
public sealed record StorageUploadResult(
    Guid FileId,
    string Bucket,
    string ObjectKey,
    long SizeBytes,
    string ContentType
);

/// <summary>Sube el archivo generado al bucket temporal (IAM MinIO propia). Documents nunca guarda bytes.
/// Pedir el guardado a CloudStorage lo hace el handler con su propio bus (DOC-3).</summary>
public interface IDocumentStorageClient
{
    /// <summary>RequestingService con que Documents firma sus SaveFileRequested (y que CloudStorage devuelve al rechazar).</summary>
    const string RequestingService = "documents";

    Task<Result<StorageUploadResult>> UploadAsync(
        Guid fileId,
        byte[] content,
        string fileName,
        string contentType,
        CancellationToken ct = default
    );
}
