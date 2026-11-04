using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Requests.Preparer;

namespace TaxVision.Signature.Application.Requests.Queries.PreparerCandidates;

/// <summary>A quién se le puede pasar el preparador: activos de la oficina con su identidad 8879 declarada.</summary>
public sealed record ListPreparerCandidatesQuery(Guid TenantId);

/// <summary>Candidato a preparador. El PTIN/EFIN va enmascarado: basta para distinguir, no para copiarlo.</summary>
public sealed record PreparerCandidateResponse(
    Guid UserId,
    string DisplayName,
    string? TitleLabel,
    string MaskedIdentifier
);

public static class ListPreparerCandidatesHandler
{
    public static async Task<IReadOnlyList<PreparerCandidateResponse>> Handle(
        ListPreparerCandidatesQuery query,
        ISignatureRequestStaffWorkRepository staffWork,
        IAuthzUserPermissionsProjectionRepository permissions,
        CancellationToken ct
    )
    {
        var userIds = await staffWork.ListUsersWithPreparerIdentityAsync(query.TenantId, ct);
        var candidates = new List<PreparerCandidateResponse>();
        foreach (var userId in userIds)
        {
            var member = await permissions.GetAsync(query.TenantId, userId, ct);
            if (member is not { IsActive: true })
                continue;

            var identity = await staffWork.FindLatestPreparerIdentityAsync(query.TenantId, userId, ct);
            if (identity is null)
                continue;

            candidates.Add(
                new PreparerCandidateResponse(
                    userId,
                    identity.DisplayName,
                    identity.TitleLabel,
                    PreparerIdentifierMask.Mask(identity.PtinOrEfin)
                )
            );
        }

        return candidates.OrderBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
