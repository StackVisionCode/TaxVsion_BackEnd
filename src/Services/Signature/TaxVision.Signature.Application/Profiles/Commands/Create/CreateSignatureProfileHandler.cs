using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Application.Profiles.Authorization;
using TaxVision.Signature.Application.Requests.Public;
using TaxVision.Signature.Domain.Profiles;

namespace TaxVision.Signature.Application.Profiles.Commands.Create;

public static class CreateSignatureProfileHandler
{
    public static async Task<Result<SignatureProfileResponse>> Handle(
        CreateSignatureProfileCommand cmd,
        ISignatureProfileRepository repository,
        ITenantSignatureSettingsRepository settingsRepository,
        ISignatureCloudStorageClient storage,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        // Firma de oficina (sin dueño) solo la crea un admin; la personal solo para uno mismo.
        var authorize = SignatureProfileAuthorization.EnsureCanManageScope(
            cmd.OwnerUserId,
            cmd.ActorUserId,
            cmd.ActorIsAdmin
        );
        if (authorize.IsFailure)
            return Result.Failure<SignatureProfileResponse>(authorize.Error);

        // Con el toggle apagado, un empleado no-admin no puede crear su firma personal (solo la de oficina).
        if (cmd.OwnerUserId is not null)
        {
            var settings = await settingsRepository.GetByTenantIdAsync(cmd.TenantId, ct);
            if (!SignatureVisibilityPolicy.CanUsePersonal(cmd.ActorIsAdmin, settings))
                return Result.Failure<SignatureProfileResponse>(SignatureProfileErrors.OwnSignatureDisabled);
        }

        // Tope de firmas activas por ámbito (antes de subir nada): archivar una libera cupo.
        var existing = await repository.ListByOwnerAsync(cmd.TenantId, cmd.OwnerUserId, includeArchived: false, ct);
        if (existing.Count >= SignatureProfile.MaxActiveProfilesPerScope)
            return Result.Failure<SignatureProfileResponse>(
                new Error(
                    "Signature.Profile.LimitReached",
                    $"You can keep at most {SignatureProfile.MaxActiveProfilesPerScope} signatures. Archive or delete one first."
                )
            );

        var imageValidation = SignatureImageValidator.Validate(cmd.Content);
        if (imageValidation.IsFailure)
            return Result.Failure<SignatureProfileResponse>(imageValidation.Error);

        var (width, height) = SignatureImageValidator.ReadDimensions(cmd.Content);

        // La imagen se guarda en la carpeta Signatures del dueño (usuario u oficina), reusando la
        // ruta IAM-scoped de Signature. La carpeta exige año fiscal → año en curso (la firma no es fiscal).
        var upload = new SignatureFileUpload(
            Content: cmd.Content,
            FileName: "preparer-signature.png",
            ContentType: "image/png",
            OwnerType: "Signature",
            OwnerId: cmd.OwnerUserId ?? cmd.TenantId,
            FolderType: "Signatures",
            TaxYear: DateTime.UtcNow.Year,
            ActorId: cmd.ActorUserId
        );
        var uploaded = await storage.UploadAsync(cmd.TenantId, upload, ct);
        if (uploaded.IsFailure)
            return Result.Failure<SignatureProfileResponse>(uploaded.Error);

        var created = SignatureProfile.Create(
            cmd.TenantId,
            cmd.ActorUserId,
            cmd.OwnerUserId,
            cmd.Label,
            uploaded.Value,
            width,
            height
        );
        if (created.IsFailure)
            return Result.Failure<SignatureProfileResponse>(created.Error);

        var profile = created.Value;

        // La primera firma ACTIVA del ámbito queda como default (no dejar al usuario sin default utilizable).
        if (existing.Count == 0)
            profile.MarkDefault();

        await repository.AddAsync(profile, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(SignatureProfileResponse.From(profile));
    }
}
