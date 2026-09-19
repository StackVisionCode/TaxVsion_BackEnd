using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Requests.Public;

/// <summary>
/// Sube la imagen de firma del firmante a CloudStorage y devuelve su <c>FileId</c>. Fases:
/// (1) resolver token + epoch, (2) rechazar si el firmante ya no está pendiente, (3) validar
/// que el payload es un PNG acotado, (4) subir a la carpeta "Signatures" del envelope. No muta
/// el aggregate: la evidencia se ancla después, en <see cref="SubmitSignatureHandler"/>.
/// </summary>
public static class AttachSignatureImageHandler
{
    public static async Task<Result<Guid>> Handle(
        AttachSignatureImageCommand cmd,
        ISigningTokenService tokenService,
        ISignatureRequestRepository repository,
        IJtiDenylist denylist,
        ISignatureCloudStorageClient storage,
        CancellationToken ct
    )
    {
        var resolution = await PublicTokenResolver.ResolveAsync(cmd.Token, tokenService, repository, denylist, ct);
        if (resolution.IsFailure)
            return Result.Failure<Guid>(resolution.Error);

        var (request, signer) = (resolution.Value.Request, resolution.Value.Signer);
        if (signer.Status != SignerStatus.Pending)
            return Result.Failure<Guid>(
                new Error("Signature.Image.NotPending", "This signer can no longer submit a signature.")
            );

        var validation = SignatureImageValidator.Validate(cmd.Content);
        if (validation.IsFailure)
            return Result.Failure<Guid>(validation.Error);

        var upload = new SignatureFileUpload(
            Content: cmd.Content,
            FileName: $"signature-{signer.Id:D}.png",
            ContentType: "image/png",
            // La imagen de firma es evidencia interna del envelope (no un entregable del cliente):
            // dueño = la propia Signature request, nunca la carpeta de un Customer.
            OwnerType: "Signature",
            OwnerId: request.Id,
            FolderType: "Signatures",
            // La carpeta "Signatures" de CloudStorage EXIGE año fiscal (File.YearRequired) — igual que el
            // sellado. Se usa el año de creación de la solicitud (la firma se captura dentro de ese ciclo).
            TaxYear: request.CreatedAtUtc.Year,
            ActorId: request.CreatedByUserId
        );

        return await storage.UploadAsync(request.TenantId, upload, ct);
    }
}
