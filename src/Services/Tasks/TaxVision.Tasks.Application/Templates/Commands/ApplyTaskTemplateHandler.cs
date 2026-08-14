using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Series.Abstractions;
using TaxVision.Tasks.Application.Tasks;
using TaxVision.Tasks.Application.Templates.Abstractions;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.Templates;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Application.Templates.Commands;

/// <param name="AllowDuplicate">
/// Aplicar el mismo encargo dos veces al mismo cliente y año casi siempre es un error de dedo, así
/// que se rechaza por defecto. Cuando de verdad hace falta —un 1040 enmendado— hay que pedirlo.
/// </param>
public sealed record ApplyTaskTemplateCommand(
    Guid TenantId,
    Guid ByUserId,
    Guid TemplateId,
    Guid? AssigneeUserId,
    Guid? CustomerId,
    int? TaxYear,
    DateTime DueAtUtc,
    string? TimeZoneId,
    bool AllowDuplicate
);

/// <summary>
/// Las N tareas y las M aristas se guardan en un solo <c>SaveChanges</c>: media plantilla aplicada
/// —tareas sin sus dependencias— dejaría al preparador con seis tareas ejecutables a la vez, que es
/// justo lo contrario de lo que la plantilla promete.
/// </summary>
public static class ApplyTaskTemplateHandler
{
    public static async Task<Result<TemplateApplicationResponse>> Handle(
        ApplyTaskTemplateCommand command,
        ITaskTemplateRepository templates,
        ITaskTemplateInstantiator instantiator,
        ITaskSeriesRepository seriesRepository,
        ITaskSeriesMaterializer materializer,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var template = await templates.GetByIdAsync(command.TenantId, command.TemplateId, ct);
        if (template.IsFailure)
            return Result.Failure<TemplateApplicationResponse>(template.Error);

        var duplicate = await EnsureNotAlreadyAppliedAsync(command, templates, ct);
        if (duplicate.IsFailure)
            return Result.Failure<TemplateApplicationResponse>(duplicate.Error);

        var applied = template.Value.IsRecurring
            ? await OpenSeriesAsync(command, template.Value, seriesRepository, materializer, ct)
            : InstantiateGraph(command, template.Value, instantiator);
        if (applied.IsFailure)
            return Result.Failure<TemplateApplicationResponse>(applied.Error);

        await unitOfWork.SaveChangesAsync(ct);

        return applied;
    }

    private static Result<TemplateApplicationResponse> InstantiateGraph(
        ApplyTaskTemplateCommand command,
        TaskTemplate template,
        ITaskTemplateInstantiator instantiator
    )
    {
        var instantiated = instantiator.Instantiate(template, ToApplication(command));

        return instantiated.IsFailure
            ? Result.Failure<TemplateApplicationResponse>(instantiated.Error)
            : Result.Success(ToResponse(command.TemplateId, instantiated.Value));
    }

    /// <summary>
    /// La trimestral no instancia cuatro tareas de golpe: abre la serie y materializa la primera.
    /// Las otras tres las trae la regla al cerrar cada una, que es lo que evita que el preparador vea
    /// en enero el trabajo de septiembre.
    /// </summary>
    private static async Task<Result<TemplateApplicationResponse>> OpenSeriesAsync(
        ApplyTaskTemplateCommand command,
        TaskTemplate template,
        ITaskSeriesRepository seriesRepository,
        ITaskSeriesMaterializer materializer,
        CancellationToken ct
    )
    {
        var blueprint = BuildBlueprint(command, template);
        if (blueprint.IsFailure)
            return Result.Failure<TemplateApplicationResponse>(blueprint.Error);

        var series = TaskSeries.Create(
            command.TenantId,
            command.ByUserId,
            template.Recurrence!,
            template.RecurrenceMode,
            blueprint.Value,
            command.DueAtUtc,
            null,
            null,
            DateTime.UtcNow
        );
        if (series.IsFailure)
            return Result.Failure<TemplateApplicationResponse>(series.Error);

        seriesRepository.Add(series.Value);
        var first = await materializer.MaterializeNextAsync(series.Value, null, null, ct);
        if (first.IsFailure)
            return Result.Failure<TemplateApplicationResponse>(first.Error);

        first.Value.AttachToTemplate(template.Id);

        return Result.Success(
            new TemplateApplicationResponse(template.Id, 1, 0, first.Value.Id, [first.Value.Id], series.Value.Id)
        );
    }

    private static Result<TaskItemBlueprint> BuildBlueprint(ApplyTaskTemplateCommand command, TaskTemplate template)
    {
        var step = template.Steps[0];
        var reference = TaskReference.Create(command.CustomerId, command.TaxYear);

        return reference.IsFailure
            ? Result.Failure<TaskItemBlueprint>(reference.Error)
            : Result.Success(
                new TaskItemBlueprint
                {
                    Title = step.Title,
                    Description = step.Description,
                    Priority = step.Priority,
                    Reference = reference.Value,
                    Estimated = step.Estimated,
                    AssigneeUserId = command.AssigneeUserId ?? command.ByUserId,
                    IsStatutory = step.IsStatutory,
                }
            );
    }

    private static async Task<Result> EnsureNotAlreadyAppliedAsync(
        ApplyTaskTemplateCommand command,
        ITaskTemplateRepository templates,
        CancellationToken ct
    )
    {
        if (command.AllowDuplicate)
            return Result.Success();

        var applied = await templates.WasAppliedAsync(
            command.TenantId,
            command.TemplateId,
            command.CustomerId,
            command.TaxYear,
            ct
        );

        return applied ? Result.Failure(TaskErrors.Template.AlreadyApplied) : Result.Success();
    }

    private static TemplateApplication ToApplication(ApplyTaskTemplateCommand command) =>
        new(
            command.ByUserId,
            command.AssigneeUserId,
            command.CustomerId,
            command.TaxYear,
            command.DueAtUtc,
            command.TimeZoneId,
            DateTime.UtcNow
        );

    /// <summary>
    /// El primero es el único ejecutable: los demás nacen bloqueados. Devolverlo evita que el
    /// frontend tenga que adivinar cuál mostrar arriba.
    /// </summary>
    private static TemplateApplicationResponse ToResponse(Guid templateId, TemplateInstantiation instantiation)
    {
        var ordered = instantiation.Tasks.OrderBy(t => t.Due!.DueAtUtc).ToList();

        return new TemplateApplicationResponse(
            templateId,
            ordered.Count,
            instantiation.Dependencies.Count,
            ordered[0].Id,
            [.. ordered.Select(t => t.Id)],
            null
        );
    }
}
