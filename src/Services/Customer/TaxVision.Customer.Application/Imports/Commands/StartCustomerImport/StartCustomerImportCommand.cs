using TaxVision.Customer.Domain.Imports;

namespace TaxVision.Customer.Application.Imports.Commands.StartCustomerImport;

/// <summary>
/// Comando para encolar un job de bulk import. El controller construye este comando con:
///   - TenantId y CreatedByUserId desde el JWT
///   - IdempotencyKey desde el header HTTP "Idempotency-Key"
///   - FileBytes desde el multipart upload
///   - FileName y SourceKind detectados desde la extension
/// </summary>
public sealed record StartCustomerImportCommand(
    Guid TenantId,
    Guid CreatedByUserId,
    string IdempotencyKey,
    DuplicateStrategy Strategy,
    ImportSourceKind SourceKind,
    string SourceFileName,
    byte[] FileBytes
);
