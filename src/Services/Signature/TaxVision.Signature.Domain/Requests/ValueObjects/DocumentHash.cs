using BuildingBlocks.Results;

namespace TaxVision.Signature.Domain.Requests.ValueObjects;

/// <summary>
/// Huella SHA-256 (RFC 6234) del documento en formato hex-lowercase de 64 caracteres.
/// Alimenta las reglas de integridad documental (Signature Design §20) y aparece en el
/// Certificate of Completion tanto pre-firma como post-firma.
/// </summary>
public sealed record DocumentHash
{
    public const int ExpectedLength = 64;

    public string Value { get; }

    private DocumentHash(string value) => Value = value;

    public static Result<DocumentHash> Create(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return Result.Failure<DocumentHash>(
                new Error("Signature.DocumentHash.Empty", "Document hash is required.")
            );

        var normalized = candidate.Trim().ToLowerInvariant();
        if (normalized.Length != ExpectedLength)
            return Result.Failure<DocumentHash>(
                new Error(
                    "Signature.DocumentHash.Length",
                    $"Document hash must be exactly {ExpectedLength} hex characters."
                )
            );

        for (var i = 0; i < normalized.Length; i++)
        {
            var c = normalized[i];
            var isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
            if (!isHex)
                return Result.Failure<DocumentHash>(
                    new Error("Signature.DocumentHash.Format", "Document hash must contain only hex characters.")
                );
        }

        return Result.Success(new DocumentHash(normalized));
    }

    public override string ToString() => Value;
}
