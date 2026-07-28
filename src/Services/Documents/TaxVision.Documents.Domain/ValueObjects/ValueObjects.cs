using BuildingBlocks.Results;

namespace TaxVision.Documents.Domain.ValueObjects;

/// <summary>Tipo de documento (Invoice, Receipt, CreditNote, …). String extensible — Documents no
/// codifica reglas fiscales por tipo, solo lo usa para resolver plantilla/validación estructural.</summary>
public sealed record DocumentType
{
    public string Value { get; }

    private DocumentType(string value) => Value = value;

    public static Result<DocumentType> Create(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 60)
            return Result.Failure<DocumentType>(
                new Error("Documents.DocumentType.Invalid", "DocumentType is required and cannot exceed 60 characters.")
            );

        return Result.Success(new DocumentType(normalized));
    }

    public override string ToString() => Value;
}

/// <summary>Clave de plantilla documental (p.ej. billing.invoice.v1). Propiedad de Documents.</summary>
public sealed record TemplateKey
{
    public string Value { get; }

    private TemplateKey(string value) => Value = value;

    public static Result<TemplateKey> Create(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 100)
            return Result.Failure<TemplateKey>(
                new Error("Documents.TemplateKey.Invalid", "TemplateKey is required and cannot exceed 100 characters.")
            );

        return Result.Success(new TemplateKey(normalized));
    }

    public override string ToString() => Value;
}

/// <summary>Referencia opaca al archivo permanente en CloudStorage. Documents nunca guarda bytes.</summary>
public sealed record StorageReference(Guid FileId, string ContentType, long SizeBytes, string? ChecksumSha256);

/// <summary>Recurso externo del servicio dueño al que pertenece la generación (p.ej. una factura).</summary>
public sealed record GenerationOwner(string OwnerType, Guid OwnerId);

/// <summary>SHA-256 hex del contenido generado, para deduplicación/verificación.</summary>
public sealed record ContentHash
{
    public string Value { get; }

    private ContentHash(string value) => Value = value;

    public static Result<ContentHash> Create(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (normalized is null || normalized.Length != 64 || !normalized.All(Uri.IsHexDigit))
            return Result.Failure<ContentHash>(
                new Error("Documents.ContentHash.Invalid", "ContentHash must be a 64-character SHA-256 hex digest.")
            );

        return Result.Success(new ContentHash(normalized));
    }

    public override string ToString() => Value;
}
