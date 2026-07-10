using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Templates;

namespace TaxVision.Signature.Application.Templates.Commands.Create;

public static class CreateSignatureTemplateHandler
{
    public static async Task<Result<SignatureTemplateResponse>> Handle(
        CreateSignatureTemplateCommand cmd,
        ISignatureTemplateRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var factoryResult = CreateDraft(cmd);
        if (factoryResult.IsFailure)
            return Result.Failure<SignatureTemplateResponse>(factoryResult.Error);

        await PersistAsync(factoryResult.Value, repository, unitOfWork, ct);
        return Result.Success(SignatureTemplateResponse.From(factoryResult.Value));
    }

    private static Result<SignatureTemplate> CreateDraft(CreateSignatureTemplateCommand cmd) =>
        SignatureTemplate.CreateDraft(
            tenantId: cmd.TenantId,
            createdByUserId: cmd.CreatedByUserId,
            title: cmd.Title,
            description: cmd.Description,
            category: cmd.Category,
            defaultTokenExpirationHours: cmd.DefaultTokenExpirationHours,
            requiresSequentialSigning: cmd.RequiresSequentialSigning,
            requiresConsent: cmd.RequiresConsent,
            generateCertificate: cmd.GenerateCertificate
        );

    private static async Task PersistAsync(
        SignatureTemplate template,
        ISignatureTemplateRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        await repository.AddAsync(template, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
