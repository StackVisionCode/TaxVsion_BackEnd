using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Domain.Series;

/// <summary>
/// Lo que se copia en cada ocurrencia. Es un molde, no una tarea: no tiene estado, ni fechas, ni
/// adjuntos. Editarlo cambia las ocurrencias futuras y no toca las ya materializadas.
///
/// <para>
/// Propiedades <c>init</c> y no un record posicional: <see cref="Reference"/> es un owned type
/// anidado y EF no puede bindear owned types a parámetros de constructor.
/// </para>
/// </summary>
public sealed record TaskItemBlueprint
{
    public required TaskTitle Title { get; init; }
    public TaskDescription? Description { get; init; }
    public required TaskPriority Priority { get; init; }
    public required TaskReference Reference { get; init; }
    public EstimatedHours? Estimated { get; init; }
    public required Guid AssigneeUserId { get; init; }
    public required bool IsStatutory { get; init; }
}
