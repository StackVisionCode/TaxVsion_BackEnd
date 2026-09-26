using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Categories;
using TaxVision.Signature.Domain.Templates;

namespace TaxVision.Signature.Application.Templates.Commands.Create;

public static class CreateSignatureTemplateHandler
{
    public static async Task<Result<SignatureTemplateResponse>> Handle(
        CreateSignatureTemplateCommand cmd,
        ISignatureTemplateRepository repository,
        IUnitOfWork unitOfWork,
        ISignatureCategoryResolver categoryResolver,
        CancellationToken ct
    )
    {
        var category = await categoryResolver.ResolveAsync(cmd.TenantId, cmd.Category, ct);
        if (category.IsFailure)
            return Result.Failure<SignatureTemplateResponse>(category.Error);

        var factoryResult = CreateDraft(cmd, category.Value);
        if (factoryResult.IsFailure)
            return Result.Failure<SignatureTemplateResponse>(factoryResult.Error);

        await PersistAsync(factoryResult.Value, repository, unitOfWork, ct);
        return Result.Success(SignatureTemplateResponse.From(factoryResult.Value));
    }

    private static Result<SignatureTemplate> CreateDraft(CreateSignatureTemplateCommand cmd, string category) =>
        SignatureTemplate.CreateDraft(
            tenantId: cmd.TenantId,
            createdByUserId: cmd.CreatedByUserId,
            title: cmd.Title,
            description: cmd.Description,
            category: category,
            defaultTokenExpirationHours: cmd.DefaultTokenExpirationHours,
            requiresSequentialSigning: cmd.RequiresSequentialSigning,
            requiresConsent: cmd.RequiresConsent,
            generateCertificate: cmd.GenerateCertificate,
            baseDocumentFileId: cmd.BaseDocumentFileId,
            sendSignedDocumentToSigners: cmd.SendSignedDocumentToSigners,
            sendCertificateToSigners: cmd.SendCertificateToSigners,
            autoRemindersEnabled: cmd.AutoRemindersEnabled,
            reminderIntervalHours: cmd.ReminderIntervalHours
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
