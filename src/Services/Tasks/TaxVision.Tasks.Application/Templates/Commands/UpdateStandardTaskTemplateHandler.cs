using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Common.Abstractions;
using TaxVision.Tasks.Application.Templates.Abstractions;
using TaxVision.Tasks.Application.Templates.Seed;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Templates.Commands;

public sealed record UpdateStandardTaskTemplateCommand(Guid TenantId, Guid TemplateId, Guid ByUserId);

/// <summary>
/// Pone una estándar instalada en su versión vigente, cuando el administrador lo confirma (D-23). Si conserva un nombre
/// del catálogo, toma el nombre actual; si la oficina la renombró, el nombre se respeta.
/// </summary>
public static class UpdateStandardTaskTemplateHandler
{
    public static async Task<Result<TaskTemplateResponse>> Handle(
        UpdateStandardTaskTemplateCommand command,
        ITaskTemplateRepository templates,
        IUnitOfWork unitOfWork,
        IOfficeTimeZone zones,
        CancellationToken ct
    )
    {
        var found = await templates.GetByIdAsync(command.TenantId, command.TemplateId, ct);
        if (found.IsFailure)
            return Result.Failure<TaskTemplateResponse>(found.Error);
        if (StandardTaxTemplates.ByKey(found.Value.StandardKey) is not { } standard)
            return Result.Failure<TaskTemplateResponse>(TaskErrors.Template.NotStandard);

        var template = found.Value;
        var zoneId = template.Recurrence?.TimeZoneId ?? (await zones.ResolveAsync(command.TenantId, ct)).Id;
        var applied = StandardTemplateBuilder.ApplyVersion(template, standard, zoneId);
        if (applied.IsFailure)
            return Result.Failure<TaskTemplateResponse>(applied.Error);
        if (standard.PreviousNames.Contains(template.Name, StringComparer.OrdinalIgnoreCase))
            template.Rename(standard.Name, standard.Description, DateTime.UtcNow);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(TaskTemplateResponse.From(template));
    }
}
