using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Emailing;

namespace TaxVision.Notification.Application.Email.Templates.Commands;

/// <summary>Archiva una plantilla (no borra físicamente las publicadas).</summary>
public sealed record ArchiveEmailTemplateCommand(Guid TemplateId, Guid? TenantId, bool IsPlatformAdmin);

public static class ArchiveEmailTemplateHandler
{
    public static async Task<Result> Handle(
        ArchiveEmailTemplateCommand command,
        IEmailTemplateRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var template = await repository.GetByIdAsync(command.TemplateId, command.TenantId, ct);
        if (template is null)
            return Result.Failure(new Error("EmailTemplate.NotFound", "Template not found."));

        if (template.Scope == EmailScope.System && !command.IsPlatformAdmin)
            return Result.Failure(
                new Error("EmailTemplate.Forbidden", "Only platform administrators can manage system templates.")
            );

        var result = template.Archive();
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
