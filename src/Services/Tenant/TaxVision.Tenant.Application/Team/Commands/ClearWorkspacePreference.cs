using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Tenant.Application.Team.Abstractions;
using TaxVision.Tenant.Domain.Team;

namespace TaxVision.Tenant.Application.Team.Commands;

/// <summary>Restablece una preferencia. <c>OwnerUserId</c> nulo = quitar la plantilla de la oficina.</summary>
public sealed record ClearWorkspacePreferenceCommand(
    Guid TenantId,
    Guid? OwnerUserId,
    Guid ActorUserId,
    string Key,
    long ExpectedVersion
);

/// <summary>
/// La fila queda sin contenido y con la versión siguiente; no se borra. Sin fila y versión 0, o fila ya vacía con
/// su versión: no hace nada.
/// </summary>
public static class ClearWorkspacePreferenceHandler
{
    public static async Task<Result<WorkspacePreferenceView>> Handle(
        ClearWorkspacePreferenceCommand command,
        IWorkspacePreferenceRepository preferences,
        IUnitOfWork unitOfWork,
        TimeProvider clock,
        ILogger<WorkspacePreference> logger,
        CancellationToken ct
    )
    {
        if (!WorkspacePreferenceKeys.IsKnown(command.Key))
            return Result.Failure<WorkspacePreferenceView>(WorkspacePreferenceErrors.UnknownKey);

        var scope = new WorkspacePreferenceScope(
            command.TenantId,
            command.OwnerUserId,
            command.ActorUserId,
            command.Key
        );
        var row = await scope.TargetAsync(preferences, ct);
        var cleared =
            row?.Clear(command.ExpectedVersion, clock.GetUtcNow().UtcDateTime)
            ?? (
                command.ExpectedVersion == 0
                    ? Result.Success(false)
                    : Result.Failure<bool>(WorkspacePreferenceErrors.VersionConflict)
            );
        if (cleared.IsFailure)
            return Result.Failure<WorkspacePreferenceView>(cleared.Error);

        if (cleared.Value)
        {
            await unitOfWork.SaveChangesAsync(ct);
            WorkspacePreferenceScope.LogOfficeChange(logger, command.OwnerUserId, command.ActorUserId, row!, "removed");
        }
        return Result.Success(await scope.ViewAsync(preferences, row, ct));
    }
}
