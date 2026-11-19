using BuildingBlocks.Results;

namespace TaxVision.Tenant.Application.Team;

public static class TeamErrors
{
    public static readonly Error ActivityForbidden = new(
        "Team.Activity.Forbidden",
        "You can only see your own activity unless you can view the team's work."
    );

    public static readonly Error InvalidRange = new(
        "Team.Activity.InvalidRange",
        "The 'from' instant must be earlier than 'to'."
    );

    public static readonly Error UnknownVerbFilter = new(
        "Team.Activity.UnknownVerbFilter",
        "One or more activity verbs are not known."
    );
}
