using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Domain.Labels;

/// <summary>
/// Catálogo de presentación por tenant. Cada label declara a qué <see cref="TaskItemStatus"/>
/// corresponde: el motor lee siempre el enum, nunca el nombre. Un tenant puede llamarle «Terminado»
/// a lo que quiera y <c>Status == Completed</c> no cambia.
/// </summary>
/// <remarks>
/// <see cref="TenantEntity"/> y no <see cref="AggregateRoot"/>: es configuración de la firma y no
/// emite eventos de dominio.
/// </remarks>
public sealed class TaskLabel : TenantEntity
{
    public TaskLabelCode Code { get; private set; } = default!;
    public string DisplayName { get; private set; } = default!;
    public LabelColor Color { get; private set; } = default!;
    public TaskItemStatus MapsToStatus { get; private set; }
    public int SortOrder { get; private set; }

    public const int DisplayNameMaxLength = 60;

    private TaskLabel() { }

    public static Result<TaskLabel> Create(
        Guid tenantId,
        TaskLabelCode code,
        string? displayName,
        LabelColor color,
        TaskItemStatus mapsToStatus,
        int sortOrder
    )
    {
        var name = Normalize(displayName);
        if (name.IsFailure)
            return Result.Failure<TaskLabel>(name.Error);

        var label = new TaskLabel
        {
            Code = code,
            DisplayName = name.Value,
            Color = color,
            MapsToStatus = mapsToStatus,
            SortOrder = sortOrder,
        };
        label.SetTenant(tenantId);
        return Result.Success(label);
    }

    /// <summary>El <see cref="Code"/> no se renombra: es lo que el front tiene guardado.</summary>
    public Result Rename(string? displayName, LabelColor color, TaskItemStatus mapsToStatus, int sortOrder)
    {
        var name = Normalize(displayName);
        if (name.IsFailure)
            return Result.Failure(name.Error);

        DisplayName = name.Value;
        Color = color;
        MapsToStatus = mapsToStatus;
        SortOrder = sortOrder;
        return Result.Success();
    }

    private static Result<string> Normalize(string? displayName)
    {
        var trimmed = displayName?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return Result.Failure<string>(TaskErrors.Label.DisplayNameEmpty);

        return trimmed.Length > DisplayNameMaxLength
            ? Result.Failure<string>(TaskErrors.Label.DisplayNameTooLong)
            : Result.Success(trimmed);
    }
}
