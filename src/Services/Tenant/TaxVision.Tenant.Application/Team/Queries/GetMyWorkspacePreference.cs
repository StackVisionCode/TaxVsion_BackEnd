using BuildingBlocks.Results;
using TaxVision.Tenant.Application.Team.Abstractions;
using TaxVision.Tenant.Domain.Team;

namespace TaxVision.Tenant.Application.Team.Queries;

/// <summary>Preferencia efectiva del que llama. El tenant y el usuario salen del JWT.</summary>
public sealed record GetMyWorkspacePreferenceQuery(Guid TenantId, Guid UserId, string Key);

/// <summary>Nunca 404: sin fila propia ni plantilla responde la de fábrica (<c>source: default</c>).</summary>
public static class GetMyWorkspacePreferenceHandler
{
    public static async Task<Result<WorkspacePreferenceView>> Handle(
        GetMyWorkspacePreferenceQuery query,
        IWorkspacePreferenceRepository preferences,
        CancellationToken ct
    )
    {
        if (!WorkspacePreferenceKeys.IsKnown(query.Key))
            return Result.Failure<WorkspacePreferenceView>(WorkspacePreferenceErrors.UnknownKey);

        var own = await preferences.GetForUserAsync(query.TenantId, query.UserId, query.Key, ct);
        var office = await preferences.GetOfficeDefaultAsync(query.TenantId, query.Key, ct);
        return Result.Success(WorkspacePreferenceView.Compose(query.Key, own, office));
    }
}
