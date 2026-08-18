using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Domain.Dependencies;

/// <summary>La arista. Lo que necesita mirar otras filas se valida en el handler.</summary>
public sealed class TaskDependency : BaseEntity, ITenantOwned
{
    private TaskDependency() { }

    public Guid TenantId { get; private set; }

    /// <summary>La sucesora: la que queda bloqueada.</summary>
    public Guid TaskId { get; private set; }

    /// <summary>La predecesora: la que bloquea.</summary>
    public Guid DependsOnTaskId { get; private set; }

    public DependencyType Type { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public void SetTenant(Guid tenantId) => TenantId = tenantId;

    public static Result<TaskDependency> Create(
        Guid tenantId,
        Guid taskId,
        Guid dependsOnTaskId,
        Guid createdByUserId,
        DateTime nowUtc
    )
    {
        if (tenantId == Guid.Empty || taskId == Guid.Empty || dependsOnTaskId == Guid.Empty)
            return Result.Failure<TaskDependency>(TaskErrors.Dependency.IdentifiersRequired);

        if (taskId == dependsOnTaskId)
            return Result.Failure<TaskDependency>(TaskErrors.Dependency.SelfReference);

        var dependency = new TaskDependency
        {
            Id = Guid.NewGuid(),
            TaskId = taskId,
            DependsOnTaskId = dependsOnTaskId,
            Type = DependencyType.FinishToStart,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = nowUtc,
        };
        dependency.SetTenant(tenantId);
        return Result.Success(dependency);
    }
}
