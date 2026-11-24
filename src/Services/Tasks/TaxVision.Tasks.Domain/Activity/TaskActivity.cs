using BuildingBlocks.Domain;

namespace TaxVision.Tasks.Domain.Activity;

/// <summary>
/// Una entrada del historial de una tarea: qué pasó, quién y cuándo. Solo se añade; nunca se edita. Los detalles
/// (fechas, motivo, ids) van en <see cref="DataJson"/> y los nombres se resuelven al leer.
/// </summary>
public sealed class TaskActivity : TenantEntity
{
    public const int DataMaxLength = 2000;

    private TaskActivity() { }

    public Guid TaskId { get; private set; }
    public TaskActivityKind Kind { get; private set; }

    [UserReference(UserReferenceKind.Provenance)]
    public Guid? ActorUserId { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public string? DataJson { get; private set; }

    public static TaskActivity Record(
        Guid tenantId,
        Guid taskId,
        TaskActivityKind kind,
        Guid? actorUserId,
        DateTime occurredAtUtc,
        string? dataJson
    )
    {
        var activity = new TaskActivity
        {
            TaskId = taskId,
            Kind = kind,
            ActorUserId = actorUserId,
            OccurredAtUtc = occurredAtUtc,
            DataJson = dataJson is { Length: > DataMaxLength } ? dataJson[..DataMaxLength] : dataJson,
        };
        activity.SetTenant(tenantId);
        return activity;
    }
}
