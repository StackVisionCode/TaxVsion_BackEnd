using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Signature.Domain.Requests;

/// <summary>First time a signer viewed a specific document in the public ceremony.</summary>
public sealed class SignerDocumentView : BaseEntity
{
    private SignerDocumentView() { }

    public Guid SignerId { get; private set; }
    public Guid DocumentId { get; private set; }
    public DateTime FirstViewedAtUtc { get; private set; }

    internal static Result<SignerDocumentView> Create(Guid signerId, Guid documentId, DateTime viewedAtUtc)
    {
        if (signerId == Guid.Empty)
            return Result.Failure<SignerDocumentView>(
                new Error("Signature.DocumentView.Signer", "SignerId is required.")
            );
        if (documentId == Guid.Empty)
            return Result.Failure<SignerDocumentView>(
                new Error("Signature.DocumentView.Document", "DocumentId is required.")
            );

        return Result.Success(
            new SignerDocumentView
            {
                Id = Guid.NewGuid(),
                SignerId = signerId,
                DocumentId = documentId,
                FirstViewedAtUtc = viewedAtUtc,
            }
        );
    }
}
