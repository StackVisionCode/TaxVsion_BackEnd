using BuildingBlocks.Results;

namespace TaxVision.Tenant.Domain.Team;

public static class ActivityEntryErrors
{
    public static readonly Error TenantRequired = new("Team.Activity.TenantRequired", "The office is required.");

    public static readonly Error EventRequired = new("Team.Activity.EventRequired", "The source event is required.");

    public static readonly Error ActorRequired = new("Team.Activity.ActorRequired", "The acting user is required.");

    public static readonly Error UnknownVerb = new("Team.Activity.UnknownVerb", "The activity verb is not known.");

    public static readonly Error ResourceRequired = new("Team.Activity.ResourceRequired", "The resource is required.");

    public static readonly Error VisibilityInvalid = new(
        "Team.Activity.VisibilityInvalid",
        "The activity visibility is not valid."
    );

    public static readonly Error OccurredAtRequired = new(
        "Team.Activity.OccurredAtRequired",
        "The time of the action is required."
    );
}
