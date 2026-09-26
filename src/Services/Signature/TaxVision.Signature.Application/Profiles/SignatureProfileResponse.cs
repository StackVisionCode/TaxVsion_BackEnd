using TaxVision.Signature.Domain.Profiles;

namespace TaxVision.Signature.Application.Profiles;

/// <summary>
/// Vista de una firma reutilizable para el frontend. Devuelve el <c>FileId</c> (el front pide la
/// URL de descarga a CloudStorage, igual que sealed/original) — nunca los bytes de la imagen.
/// </summary>
public sealed record SignatureProfileResponse(
    Guid Id,
    Guid? OwnerUserId,
    bool IsOffice,
    string Label,
    Guid FileId,
    int Width,
    int Height,
    bool IsDefault,
    bool IsArchived
)
{
    public static SignatureProfileResponse From(SignatureProfile profile) =>
        new(
            profile.Id,
            profile.OwnerUserId,
            profile.IsOffice,
            profile.Label,
            profile.FileId,
            profile.Width,
            profile.Height,
            profile.IsDefault,
            profile.IsArchived
        );
}
