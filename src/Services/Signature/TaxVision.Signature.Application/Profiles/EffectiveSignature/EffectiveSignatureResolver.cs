using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Profiles;

namespace TaxVision.Signature.Application.Profiles.EffectiveSignature;

public sealed class EffectiveSignatureResolver(
    ITenantSignatureSettingsRepository settingsRepository,
    ISignatureProfileRepository profileRepository
) : IEffectiveSignatureResolver
{
    public static readonly Error None = new(
        "Signature.Profile.NoEffective",
        "No signature is configured for this preparer."
    );

    public async Task<Result<SignatureProfile>> ResolveAsync(
        Guid tenantId,
        Guid preparerUserId,
        CancellationToken ct = default
    )
    {
        var settings = await settingsRepository.GetByTenantIdAsync(tenantId, ct);
        // Sin settings (tenant recién creado sin proyección aún) → se asume el default: firma propia permitida.
        var allowOwn = settings?.AllowEmployeeOwnSignature ?? true;

        if (allowOwn)
        {
            var personal = await profileRepository.GetDefaultAsync(tenantId, preparerUserId, ct);
            if (personal is not null)
                return Result.Success(personal);
        }

        var office = await profileRepository.GetDefaultAsync(tenantId, ownerUserId: null, ct);
        return office is not null ? Result.Success(office) : Result.Failure<SignatureProfile>(None);
    }
}
