using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Application.Profiles.Queries.List;

public static class ListSignatureProfilesHandler
{
    public static async Task<ListSignatureProfilesResult> Handle(
        ListSignatureProfilesQuery query,
        ISignatureProfileRepository repository,
        ITenantSignatureSettingsRepository settingsRepository,
        CancellationToken ct
    )
    {
        var settings = await settingsRepository.GetByTenantIdAsync(query.TenantId, ct);
        var canUsePersonal = SignatureVisibilityPolicy.CanUsePersonal(query.ActorIsAdmin, settings);
        var profiles = await repository.ListVisibleAsync(
            query.TenantId,
            query.UserId,
            canUsePersonal,
            query.IncludeArchived,
            ct
        );
        return new ListSignatureProfilesResult(profiles.Select(SignatureProfileResponse.From).ToList(), canUsePersonal);
    }
}
