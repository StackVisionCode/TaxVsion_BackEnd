using BuildingBlocks.Contracts.Work;
using BuildingBlocks.Results;

namespace TaxVision.Calendar.Application.Appointments.Work;

public static class WorkErrors
{
    public static readonly Error InvalidUserIds = new(
        "Calendar.Work.InvalidUserIds",
        "Send between 1 and 100 valid user ids separated by commas."
    );

    public static readonly Error InvalidTitles = new(
        "Calendar.Work.InvalidTitles",
        $"Send a list of at most {WorkLimits.TitleBatch} items."
    );

    public static readonly Error InvalidKind = new(
        "Calendar.Work.InvalidKind",
        $"kind must be {WorkKinds.Appointments}."
    );
}

/// <summary>Lee <c>userIds=a,b,c</c> del lote: sin repetidos y con el tope del contrato.</summary>
public static class WorkUserIds
{
    public static Result<IReadOnlyList<Guid>> Parse(string? raw)
    {
        var parts = (raw ?? string.Empty).Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        var ids = new List<Guid>();
        foreach (var part in parts)
        {
            if (!Guid.TryParse(part, out var id))
                return Result.Failure<IReadOnlyList<Guid>>(WorkErrors.InvalidUserIds);
            if (!ids.Contains(id))
                ids.Add(id);
        }

        return ids.Count is 0 or > WorkLimits.BatchUsers
            ? Result.Failure<IReadOnlyList<Guid>>(WorkErrors.InvalidUserIds)
            : Result.Success<IReadOnlyList<Guid>>(ids);
    }
}
