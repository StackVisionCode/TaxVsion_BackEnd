using BuildingBlocks.Results;

namespace TaxVision.Signature.Domain.Requests.ValueObjects;

/// <summary>
/// Resultado de revisar el PDF original en el servidor: su hash y el número de páginas contado por el
/// servidor (no el que reporta el navegador). Es lo único que deja un documento listo para enviar.
/// </summary>
public sealed record VerifiedDocument
{
    public DocumentHash Hash { get; }
    public int PageCount { get; }

    private VerifiedDocument(DocumentHash hash, int pageCount)
    {
        Hash = hash;
        PageCount = pageCount;
    }

    public static Result<VerifiedDocument> Create(DocumentHash hash, int pageCount)
    {
        ArgumentNullException.ThrowIfNull(hash);
        if (pageCount < 1)
            return Result.Failure<VerifiedDocument>(
                new Error("Signature.Document.PageCount", "A verified document must have at least one page.")
            );
        return Result.Success(new VerifiedDocument(hash, pageCount));
    }
}
