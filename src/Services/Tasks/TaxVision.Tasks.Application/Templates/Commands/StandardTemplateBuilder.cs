using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Templates.Seed;
using TaxVision.Tasks.Domain.Templates;

namespace TaxVision.Tasks.Application.Templates.Commands;

/// <summary>Construye o pone al día una plantilla con la versión vigente de una estándar.</summary>
public static class StandardTemplateBuilder
{
    public static Result<TaskTemplate> Build(
        Guid tenantId,
        Guid byUserId,
        StandardTaxTemplate standard,
        string officeZoneId
    )
    {
        var created = TaskTemplate.Create(tenantId, byUserId, standard.Name, standard.Description, DateTime.UtcNow);
        if (created.IsFailure)
            return created;

        created.Value.AdoptStandardKey(standard.Key);
        var applied = ApplyVersion(created.Value, standard, officeZoneId);
        return applied.IsFailure ? Result.Failure<TaskTemplate>(applied.Error) : created;
    }

    /// <summary>La regla y los pasos de la versión vigente. Las series ya abiertas desde la plantilla no cambian.</summary>
    public static Result ApplyVersion(TaskTemplate template, StandardTaxTemplate standard, string officeZoneId)
    {
        var recurrence = TaskTemplateStepFactory.ApplyRecurrence(
            template,
            standard.RecurrenceRule,
            officeZoneId,
            standard.RecurrenceMode
        );
        return recurrence.IsFailure ? recurrence : TaskTemplateStepFactory.ApplyTo(template, standard.Steps);
    }
}
