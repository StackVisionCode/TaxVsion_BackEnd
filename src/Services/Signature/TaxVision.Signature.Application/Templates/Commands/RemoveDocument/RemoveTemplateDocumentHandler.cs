using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Application.Templates.Commands.RemoveDocument;

public static class RemoveTemplateDocumentHandler
{
    public static async Task<Result> Handle(
        RemoveTemplateDocumentCommand command,
        ISignatureTemplateRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken
    )
    {
        var template = await repository.GetByIdAsync(command.TenantId, command.TemplateId, cancellationToken);
        if (template is null)
            return Result.Failure(
                new Error("Signature.Template.NotFound", "The signature template does not exist for this tenant.")
            );

        var result = template.RemoveDocument(command.DocumentId);
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
