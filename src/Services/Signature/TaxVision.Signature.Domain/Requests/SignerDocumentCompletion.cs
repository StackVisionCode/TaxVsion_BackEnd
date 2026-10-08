using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Signature.Domain.Requests;

/// <summary>Durable progress for one signer completing one request document.</summary>
public sealed class SignerDocumentCompletion : BaseEntity
{
    private SignerDocumentCompletion() { }

    public Guid SignerId { get; private set; }
    public Guid DocumentId { get; private set; }
    public DateTime CompletedAtUtc { get; private set; }
    public DateTime? PartialCopyRequestedAtUtc { get; private set; }
    public DateTime? PartialCopySentAtUtc { get; private set; }
    public Guid? PartialCopyFileId { get; private set; }
    public string? PartialCopyFailureReason { get; private set; }

    internal static Result<SignerDocumentCompletion> Create(Guid signerId, Guid documentId, DateTime completedAtUtc)
    {
        if (signerId == Guid.Empty)
            return Result.Failure<SignerDocumentCompletion>(
                new Error("Signature.DocumentCompletion.Signer", "SignerId is required.")
            );
        if (documentId == Guid.Empty)
            return Result.Failure<SignerDocumentCompletion>(
                new Error("Signature.DocumentCompletion.Document", "DocumentId is required.")
            );

        return Result.Success(
            new SignerDocumentCompletion
            {
                Id = Guid.NewGuid(),
                SignerId = signerId,
                DocumentId = documentId,
                CompletedAtUtc = completedAtUtc,
            }
        );
    }

    internal bool MarkPartialCopyRequested(DateTime requestedAtUtc)
    {
        if (PartialCopyRequestedAtUtc is not null)
            return false;

        PartialCopyRequestedAtUtc = requestedAtUtc;
        return true;
    }

    internal void MarkPartialCopySent(Guid fileId, DateTime sentAtUtc)
    {
        PartialCopyFileId = fileId;
        PartialCopySentAtUtc = sentAtUtc;
        PartialCopyFailureReason = null;
    }

    internal void MarkPartialCopyFailed(string reason) => PartialCopyFailureReason = reason;
}
