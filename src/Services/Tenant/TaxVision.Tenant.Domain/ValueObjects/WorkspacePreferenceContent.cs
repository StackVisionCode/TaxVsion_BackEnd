using BuildingBlocks.Results;
using TaxVision.Tenant.Domain.Team;

namespace TaxVision.Tenant.Domain.ValueObjects;

/// <summary>Un validador por clave: cada preferencia tiene su forma y su esquema.</summary>
public static class WorkspacePreferenceContent
{
    public static Result<IWorkspacePreferenceContent> Parse(string key, int schemaVersion, string rawJson) =>
        key switch
        {
            WorkspacePreferenceKeys.CrmDashboardLayout => Widen(DashboardLayout.Parse(schemaVersion, rawJson)),
            WorkspacePreferenceKeys.CrmTasksBoard => Widen(TaskBoardPreference.Parse(schemaVersion, rawJson)),
            _ => Result.Failure<IWorkspacePreferenceContent>(WorkspacePreferenceErrors.UnknownKey),
        };

    /// <summary>El esquema que se informa cuando no hay nada guardado.</summary>
    public static int DefaultSchemaOf(string key) =>
        key == WorkspacePreferenceKeys.CrmTasksBoard
            ? TaskBoardPreference.SupportedSchemaVersion
            : DashboardLayout.SupportedSchemaVersion;

    private static Result<IWorkspacePreferenceContent> Widen<T>(Result<T> parsed)
        where T : IWorkspacePreferenceContent =>
        parsed.IsFailure
            ? Result.Failure<IWorkspacePreferenceContent>(parsed.Error)
            : Result.Success<IWorkspacePreferenceContent>(parsed.Value);
}
