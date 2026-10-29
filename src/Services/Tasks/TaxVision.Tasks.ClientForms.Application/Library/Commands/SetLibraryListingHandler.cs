using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Responses;
using TaxVision.Tasks.ClientForms.Domain.Audit;

namespace TaxVision.Tasks.ClientForms.Application.Library.Commands;

public sealed record SetLibraryListingCommand(Guid ByUserId, Guid TemplateId, string LibraryKey, bool IsListed);

public static class SetLibraryListingHandler
{
    public static async Task<Result<FormTemplateDetailResponse>> Handle(
        SetLibraryListingCommand command,
        IFormTemplateRepository templates,
        IFormTemplateVersionRepository versions,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await templates.GetByIdAsync(PlatformTenant.Id, command.TemplateId, ct);
        if (found.IsFailure)
            return Result.Failure<FormTemplateDetailResponse>(found.Error);
        var outcome = command.IsListed
            ? found.Value.ListInLibrary(command.LibraryKey, PlatformTenant.Id, DateTime.UtcNow)
            : found.Value.UnlistFromLibrary(PlatformTenant.Id);
        if (outcome.IsFailure)
            return Result.Failure<FormTemplateDetailResponse>(outcome.Error);
        var action = command.IsListed ? FormAuditAction.LibraryListed : FormAuditAction.LibraryUnlisted;
        audit.Record(PlatformTenant.Id, found.Value.Id, null, action, command.ByUserId);
        await unitOfWork.SaveChangesAsync(ct);
        var published = await versions.ListAsync(PlatformTenant.Id, found.Value.Id, ct);
        return Result.Success(
            FormTemplateDetailResponse.From(
                found.Value,
                published.Select(FormTemplateVersionSummaryResponse.From).ToArray()
            )
        );
    }
}
