using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Templates.Abstractions;
using TaxVision.Tasks.Application.Templates.Seed;
using TaxVision.Tasks.Domain.Templates;

namespace TaxVision.Tasks.Application.Templates.Commands;

public sealed record InstallStandardTaskTemplatesCommand(Guid TenantId, Guid ByUserId);

/// <summary>
/// Copia el catálogo estándar al tenant, saltándose las que ya tiene por nombre. Es idempotente
/// porque la firma va a llamarlo dos veces —al abrir la cuenta y cuando alguien busque el 941 que
/// no ve— y duplicar el guion dejaría al preparador eligiendo entre dos 1040 idénticos.
/// </summary>
public static class InstallStandardTaskTemplatesHandler
{
    public static async Task<Result<IReadOnlyList<TaskTemplateResponse>>> Handle(
        InstallStandardTaskTemplatesCommand command,
        ITaskTemplateRepository templates,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var existing = await templates.ListAsync(command.TenantId, onlyActive: false, ct);
        var names = existing.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var installed = new List<TaskTemplate>();

        foreach (var standard in StandardTaxTemplates.All.Where(s => !names.Contains(s.Name)))
        {
            var built = Build(command, standard);
            if (built.IsFailure)
                return Result.Failure<IReadOnlyList<TaskTemplateResponse>>(built.Error);

            templates.Add(built.Value);
            installed.Add(built.Value);
        }

        if (installed.Count > 0)
            await unitOfWork.SaveChangesAsync(ct);

        return Result.Success<IReadOnlyList<TaskTemplateResponse>>([.. installed.Select(TaskTemplateResponse.From)]);
    }

    private static Result<TaskTemplate> Build(InstallStandardTaskTemplatesCommand command, StandardTaxTemplate standard)
    {
        var created = TaskTemplate.Create(
            command.TenantId,
            command.ByUserId,
            standard.Name,
            standard.Description,
            DateTime.UtcNow
        );
        if (created.IsFailure)
            return created;

        var recurrence = TaskTemplateStepFactory.ApplyRecurrence(
            created.Value,
            standard.RecurrenceRule,
            StandardTaxTemplates.TimeZoneId,
            standard.RecurrenceMode
        );
        if (recurrence.IsFailure)
            return Result.Failure<TaskTemplate>(recurrence.Error);

        var steps = TaskTemplateStepFactory.ApplyTo(created.Value, standard.Steps);

        return steps.IsFailure ? Result.Failure<TaskTemplate>(steps.Error) : created;
    }
}
