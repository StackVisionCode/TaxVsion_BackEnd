using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Application.Templates.Commands.AddDocument;

public static class AddTemplateDocumentHandler
{
    public static async Task<Result<TemplateDocumentCreatedResponse>> Handle(
        AddTemplateDocumentCommand command,
        ISignatureTemplateRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken
    )
    {
        var template = await repository.GetByIdAsync(command.TenantId, command.TemplateId, cancellationToken);
        if (template is null)
            return Result.Failure<TemplateDocumentCreatedResponse>(
                new Error("Signature.Template.NotFound", "The signature template does not exist for this tenant.")
            );

        var result = template.AddDocument(command.FileId, command.Title);
        if (result.IsFailure)
            return Result.Failure<TemplateDocumentCreatedResponse>(result.Error);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        var document = result.Value;
        return Result.Success(
            new TemplateDocumentCreatedResponse(document.Id, document.Order, document.FileId, document.Title)
        );
    }
}
