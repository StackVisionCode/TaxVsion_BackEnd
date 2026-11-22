using System.Text.Json.Serialization;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Tasks;

/// <summary>
/// El tablero por estado. Las columnas son los valores de <see cref="TaskItemStatus"/>, no un
/// catálogo aparte: quien quiera renombrarlas usa los labels, que son presentación.
/// </summary>
public sealed record TaskBoardResponse(IReadOnlyList<TaskBoardColumn> Columns, int TotalCount)
{
    /// <summary>Había más abiertas que el tope. No viaja en el cuerpo: el controller lo pone en una cabecera.</summary>
    [JsonIgnore]
    public bool Truncated { get; init; }
}

public sealed record TaskBoardColumn(TaskItemStatus Status, IReadOnlyList<TaskResponse> Tasks);
