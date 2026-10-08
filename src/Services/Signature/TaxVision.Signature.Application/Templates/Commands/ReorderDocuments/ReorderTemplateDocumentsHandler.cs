using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Application.Templates.Commands.ReorderDocuments;

public static class ReorderTemplateDocumentsHandler
{
    public static async Task<Result> Handle(
        ReorderTemplateDocumentsCommand command,
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

        var result = template.ReorderDocuments(command.DocumentIds);
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
