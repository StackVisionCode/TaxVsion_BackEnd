using BuildingBlocks.Contracts.Work;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;

namespace TaxVision.Customer.Application.Customers.Queries.Work;

public static class CustomerWorkErrors
{
    public static readonly Error OtherUserForbidden = new(
        "Customer.Work.Forbidden",
        "You don't have permission to view another member's clients."
    );

    public static readonly Error InvalidUserIds = new(
        "Customer.Work.InvalidUserIds",
        "Send between 1 and 100 valid user ids separated by commas."
    );
}

/// <summary>Clientes a cargo de una persona (Work Profile). Sin vencimientos: overdue/dueSoon/waiting = 0.</summary>
public static class CustomerWork
{
    public const string Provider = "customer";
    public const string Module = "clients";

    /// <summary>ABAC del listado: con el flag encendido y sin view_all, solo los asignados al que mira.</summary>
    public static Guid? VisibleTo(CustomerVisibilityOptions visibility, Guid actorUserId, bool canViewAll) =>
        visibility.Enabled && !canViewAll ? actorUserId : null;

    /// <summary>Ruta del perfil del cliente en el CRM.</summary>
    public static string Link(Guid customerId) => $"/clients/{customerId}";

    public static WorkSummaryResponse Summarize(CustomerWorkCounts counts, IReadOnlyList<CustomerWorkRow> top) =>
        new(
            Provider,
            Module,
            [
                new WorkCount(WorkKinds.ClientsPreparer, counts.AsPreparer, 0, 0, 0),
                new WorkCount(WorkKinds.ClientsAssigned, counts.Assigned, 0, 0, 0),
            ],
            [.. top.Take(WorkLimits.TopItems).Select(Item)]
        );

    public static WorkBatchResponse Batch(IReadOnlyList<Guid> userIds, IReadOnlyDictionary<Guid, int> asPreparer) =>
        new([.. userIds.Select(id => new WorkBatchRow(id, asPreparer.GetValueOrDefault(id), 0))]);

    private static WorkItem Item(CustomerWorkRow row) =>
        new(
            row.Id,
            WorkKinds.ClientsPreparer,
            row.DisplayName,
            row.Id,
            null,
            null,
            row.Status.ToString(),
            Link(row.Id)
        );
}

/// <summary>Lee <c>userIds=a,b,c</c> del lote (1 a <see cref="WorkLimits.BatchUsers"/>, sin repetidos).</summary>
public static class CustomerWorkUserIds
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
                return Result.Failure<IReadOnlyList<Guid>>(CustomerWorkErrors.InvalidUserIds);
            if (!ids.Contains(id))
                ids.Add(id);
        }
        return ids.Count is 0 or > WorkLimits.BatchUsers
            ? Result.Failure<IReadOnlyList<Guid>>(CustomerWorkErrors.InvalidUserIds)
            : Result.Success<IReadOnlyList<Guid>>(ids);
    }
}
