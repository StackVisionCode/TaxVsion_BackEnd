using BuildingBlocks.Contracts.Work;
using BuildingBlocks.Results;

namespace TaxVision.Tasks.Application.Work;

public static class WorkErrors
{
    // Mismo código que el resto de Tasks: el mapeo HTTP ya lo lleva a 403.
    public static readonly Error Forbidden = new(
        "Task.Forbidden",
        "You can only see your own work unless you can view your team's work."
    );

    public static readonly Error TooManyUsers = new(
        "Task.Work.TooManyUsers",
        $"Ask for at most {WorkLimits.BatchUsers} people at a time."
    );

    public static readonly Error InvalidTitles = new(
        "Task.Work.InvalidTitles",
        $"Send a list of at most {WorkLimits.TitleBatch} items."
    );

    public static readonly Error InvalidKind = new(
        "Task.Work.InvalidKind",
        $"kind must be {WorkKinds.Tasks}, {WorkKinds.ClientRequests} or {WorkKinds.FormRequests}."
    );

    public static readonly Error FormRequestsUnavailable = new(
        "Task.Forbidden",
        "Client forms are not available to you."
    );
}
