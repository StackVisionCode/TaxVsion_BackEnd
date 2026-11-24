using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Common.Abstractions;
using TaxVision.Tasks.Application.Templates.Abstractions;
using TaxVision.Tasks.Application.Templates.Seed;
using TaxVision.Tasks.Domain.Templates;

namespace TaxVision.Tasks.Application.Templates.Commands;

public sealed record InstallStandardTaskTemplatesCommand(Guid TenantId, Guid ByUserId);

/// <summary>Una estándar que la oficina tiene en una versión anterior: se actualiza solo si el administrador lo confirma.</summary>
public sealed record OutdatedStandardTemplate(Guid TemplateId, string Name, string StandardKey);

public sealed record StandardTemplatesInstallResponse(
    IReadOnlyList<TaskTemplateResponse> Installed,
    IReadOnlyList<OutdatedStandardTemplate> Outdated
);

/// <summary>
/// Copia el catálogo estándar al tenant, saltándose las que ya tiene. Las reconoce por clave (o, si nacieron antes de la
/// clave, por un nombre conocido, y entonces la reciben): renombrarlas o cambiar el idioma del catálogo no duplica nada.
/// Las que difieren de la versión vigente se listan como desactualizadas; nunca se reescriben solas (D-23).
/// </summary>
public static class InstallStandardTaskTemplatesHandler
{
    public static async Task<Result<StandardTemplatesInstallResponse>> Handle(
        InstallStandardTaskTemplatesCommand command,
        ITaskTemplateRepository templates,
        IUnitOfWork unitOfWork,
        IOfficeTimeZone zones,
        CancellationToken ct
    )
    {
        // Las fechas del catálogo se cuentan en la zona de la oficina (P24).
        var officeZoneId = (await zones.ResolveAsync(command.TenantId, ct)).Id;
        var existing = await templates.ListAsync(command.TenantId, onlyActive: false, ct);
        var outdated = Adopt(existing);

        var installed = new List<TaskTemplate>();
        foreach (var standard in StandardTaxTemplates.All.Where(s => !existing.Any(t => t.StandardKey == s.Key)))
        {
            var built = StandardTemplateBuilder.Build(command.TenantId, command.ByUserId, standard, officeZoneId);
            if (built.IsFailure)
                return Result.Failure<StandardTemplatesInstallResponse>(built.Error);

            templates.Add(built.Value);
            installed.Add(built.Value);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(
            new StandardTemplatesInstallResponse([.. installed.Select(TaskTemplateResponse.From)], outdated)
        );
    }

    /// <summary>Da la clave a las copias que no la tenían y devuelve las que no están en la versión vigente.</summary>
    private static List<OutdatedStandardTemplate> Adopt(IReadOnlyList<TaskTemplate> existing)
    {
        var outdated = new List<OutdatedStandardTemplate>();
        foreach (var template in existing)
        {
            if (StandardTaxTemplates.Of(template) is not { } standard)
                continue;
            // Si la oficina tuviera dos copias de la misma estándar, la clave es de la primera.
            if (template.StandardKey is null && existing.Any(t => t.StandardKey == standard.Key))
                continue;
            template.AdoptStandardKey(standard.Key);
            if (!StandardTaxTemplates.IsCurrent(template, standard))
                outdated.Add(new OutdatedStandardTemplate(template.Id, template.Name, standard.Key));
        }
        return outdated;
    }
}
