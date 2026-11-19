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
}
