using BuildingBlocks.Results;
using TaxVision.Tasks.Domain.Dependencies;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.Templates;

namespace TaxVision.Tasks.Application.Templates.Abstractions;

/// <param name="DueAtUtc">
/// El vencimiento del encargo — el 15 de abril de un 1040. Los pasos cuelgan de él por su offset.
/// </param>
public sealed record TemplateApplication(
    Guid ByUserId,
    Guid? AssigneeUserId,
    Guid? CustomerId,
    int? TaxYear,
    DateTime DueAtUtc,
    string? TimeZoneId,
    DateTime NowUtc
);

public sealed record TemplateInstantiation(IReadOnlyList<TaskItem> Tasks, IReadOnlyList<TaskDependency> Dependencies);

public interface ITaskTemplateInstantiator
{
    Result<TemplateInstantiation> Instantiate(TaskTemplate template, TemplateApplication application);
}
