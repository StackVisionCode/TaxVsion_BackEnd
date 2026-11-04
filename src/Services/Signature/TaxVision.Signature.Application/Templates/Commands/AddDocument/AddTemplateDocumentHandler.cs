using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Limits;

namespace TaxVision.Signature.Application.Templates.Commands.AddDocument;

public static class AddTemplateDocumentHandler
{
    public static async Task<Result<TemplateDocumentCreatedResponse>> Handle(
        AddTemplateDocumentCommand command,
        ISignatureTemplateRepository repository,
        ISignatureLimitsResolver limits,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken
    )
    {
        var template = await repository.GetByIdAsync(command.TenantId, command.TemplateId, cancellationToken);
        if (template is null)
            return Result.Failure<TemplateDocumentCreatedResponse>(
                new Error("Signature.Template.NotFound", "The signature template does not exist for this tenant.")
            );

        var policy = await limits.ResolveAsync(command.TenantId, cancellationToken);
        var result = template.AddDocument(policy.ForRequests, command.FileId, command.Title);
        if (result.IsFailure)
            return Result.Failure<TemplateDocumentCreatedResponse>(result.Error);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        var document = result.Value;
        return Result.Success(
            new TemplateDocumentCreatedResponse(document.Id, document.Order, document.FileId, document.Title)
        );
    }
}
