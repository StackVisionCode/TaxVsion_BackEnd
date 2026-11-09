using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Sealing;

/// <summary>Cómo se sube a CloudStorage lo que produce el sellado: documento firmado y certificado.</summary>
internal static class SealedFileUpload
{
    public static SignatureFileUpload For(SignatureRequest request, byte[] content, string title, string suffix)
    {
        var (ownerType, ownerId) = SealedDocumentOwner.Resolve(
            request.Signers.Select(signer => signer.MappedCustomerId).ToList(),
            request.Id
        );
        return new(
            Content: content,
            FileName: DocumentFileName.For(title, suffix),
            ContentType: "application/pdf",
            // Values must match CloudStorage's OwnerType / FolderType enums.
            OwnerType: ownerType,
            OwnerId: ownerId,
            FolderType: "Signatures",
            TaxYear: (request.CompletedAtUtc ?? request.CreatedAtUtc).Year,
            ActorId: request.CreatedByUserId
        );
    }
}
