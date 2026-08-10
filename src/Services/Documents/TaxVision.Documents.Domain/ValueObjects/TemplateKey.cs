using BuildingBlocks.Results;

namespace TaxVision.Documents.Domain.ValueObjects;

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
