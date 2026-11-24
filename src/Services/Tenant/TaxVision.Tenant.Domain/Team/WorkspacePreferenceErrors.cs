using BuildingBlocks.Results;

namespace TaxVision.Tenant.Domain.Team;

public static class WorkspacePreferenceErrors
{
    public static readonly Error UnknownKey = new("Team.Preference.UnknownKey", "The preference key is not known.");

    public static readonly Error UnsupportedSchema = new(
        "Team.Preference.UnsupportedSchema",
        "The preference schema version is not supported."
    );

    public static readonly Error InvalidPayload = new(
        "Team.Preference.InvalidPayload",
        "The layout must be an array of widgets with id, size and hidden."
    );

    public static readonly Error InvalidBoardPreferences = new(
        "Team.Preference.InvalidBoardPreferences",
        "Board preferences need a grouping (status or stage), up to 100 status codes per list and a sort (Due or Priority)."
    );

    public static readonly Error TooManyItems = new(
        "Team.Preference.TooManyItems",
        "The layout has more widgets than allowed."
    );

    public static readonly Error InvalidWidgetId = new(
        "Team.Preference.InvalidWidgetId",
        "A widget id must use lowercase letters, digits or dashes (up to 40)."
    );

    public static readonly Error DuplicateWidgetId = new(
        "Team.Preference.DuplicateWidgetId",
        "A widget appears more than once in the layout."
    );

    public static readonly Error InvalidWidgetSize = new(
        "Team.Preference.InvalidWidgetSize",
        "A widget size must be s, m or l."
    );

    public static readonly Error InvalidWidgetScope = new(
        "Team.Preference.InvalidWidgetScope",
        "A widget scope must be me or office."
    );

    public static readonly Error VersionConflict = new(
        "Team.Preference.VersionConflict",
        "The preference was changed somewhere else. Reload it and try again."
    );

    public static readonly Error TenantRequired = new("Team.Preference.TenantRequired", "The office is required.");

    public static readonly Error OwnerInvalid = new(
        "Team.Preference.OwnerInvalid",
        "The preference owner is not valid."
    );
}
