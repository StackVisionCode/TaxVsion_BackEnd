using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Tenant.Application.Team.Abstractions;
using TaxVision.Tenant.Domain.Team;
using TaxVision.Tenant.Domain.ValueObjects;

namespace TaxVision.Tenant.Application.Team.Commands;

/// <summary>
/// Guarda una preferencia. <c>OwnerUserId</c> nulo = la plantilla de la oficina: el controller decide el alcance y
/// exige el permiso; el handler no.
/// </summary>
public sealed record SaveWorkspacePreferenceCommand(
    Guid TenantId,
    Guid? OwnerUserId,
    Guid ActorUserId,
    string Key,
    int SchemaVersion,
    long ExpectedVersion,
    string PayloadJson
);

/// <summary>Sin fila exige versión 0 y la crea; con fila exige su versión. Contenido igual: no escribe.</summary>
public static class SaveWorkspacePreferenceHandler
{
    public static async Task<Result<WorkspacePreferenceView>> Handle(
        SaveWorkspacePreferenceCommand command,
        IWorkspacePreferenceRepository preferences,
        IUnitOfWork unitOfWork,
        TimeProvider clock,
        ILogger<WorkspacePreference> logger,
        CancellationToken ct
    )
    {
        if (!WorkspacePreferenceKeys.IsKnown(command.Key))
            return Result.Failure<WorkspacePreferenceView>(WorkspacePreferenceErrors.UnknownKey);
        var content = DashboardLayout.Parse(command.SchemaVersion, command.PayloadJson);
        if (content.IsFailure)
            return Result.Failure<WorkspacePreferenceView>(content.Error);

        var change = await ApplyAsync(command, content.Value, preferences, clock.GetUtcNow().UtcDateTime, ct);
        if (change.IsFailure)
            return Result.Failure<WorkspacePreferenceView>(change.Error);

        var (row, changed) = change.Value;
        if (changed)
        {
            await unitOfWork.SaveChangesAsync(ct);
            WorkspacePreferenceScope.LogOfficeChange(logger, command.OwnerUserId, command.ActorUserId, row, "set");
        }
        var scope = new WorkspacePreferenceScope(
            command.TenantId,
            command.OwnerUserId,
            command.ActorUserId,
            command.Key
        );
        return Result.Success(await scope.ViewAsync(preferences, row, ct));
    }

    private static async Task<Result<(WorkspacePreference Row, bool Changed)>> ApplyAsync(
        SaveWorkspacePreferenceCommand command,
        DashboardLayout content,
        IWorkspacePreferenceRepository preferences,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var scope = new WorkspacePreferenceScope(
            command.TenantId,
            command.OwnerUserId,
            command.ActorUserId,
            command.Key
        );
        var row = await scope.TargetAsync(preferences, ct);
        if (row is not null)
        {
            var replaced = row.Replace(content, command.ExpectedVersion, nowUtc);
            return replaced.IsFailure
                ? Result.Failure<(WorkspacePreference, bool)>(replaced.Error)
                : Result.Success((row, replaced.Value));
        }

        if (command.ExpectedVersion != 0)
            return Result.Failure<(WorkspacePreference, bool)>(WorkspacePreferenceErrors.VersionConflict);
        var created = WorkspacePreference.Create(command.TenantId, command.OwnerUserId, command.Key, content, nowUtc);
        if (created.IsFailure)
            return Result.Failure<(WorkspacePreference, bool)>(created.Error);
        preferences.Add(created.Value);
        return Result.Success((created.Value, true));
    }
}

/// <summary>Fila que toca un comando (la del dueño o la plantilla) y la vista que ve quien actúa.</summary>
internal sealed record WorkspacePreferenceScope(Guid TenantId, Guid? OwnerUserId, Guid ActorUserId, string Key)
{
    public Task<WorkspacePreference?> TargetAsync(IWorkspacePreferenceRepository preferences, CancellationToken ct) =>
        OwnerUserId is { } owner
            ? preferences.GetForUserAsync(TenantId, owner, Key, ct)
            : preferences.GetOfficeDefaultAsync(TenantId, Key, ct);

    public async Task<WorkspacePreferenceView> ViewAsync(
        IWorkspacePreferenceRepository preferences,
        WorkspacePreference? target,
        CancellationToken ct
    )
    {
        var own = OwnerUserId is null ? await preferences.GetForUserAsync(TenantId, ActorUserId, Key, ct) : target;
        var office = OwnerUserId is null ? target : await preferences.GetOfficeDefaultAsync(TenantId, Key, ct);
        return WorkspacePreferenceView.Compose(Key, own, office);
    }

    // Rastro de la plantilla: actor, oficina, clave y versión. Nunca el contenido.
    public static void LogOfficeChange(
        ILogger logger,
        Guid? ownerUserId,
        Guid actorUserId,
        WorkspacePreference row,
        string action
    )
    {
        if (ownerUserId is not null)
            return;
        logger.LogInformation(
            "Office default {Key} of tenant {TenantId} {Action} by {ActorId}; version {Version}.",
            row.PreferenceKey,
            row.TenantId,
            action,
            actorUserId,
            row.Version
        );
    }
}
