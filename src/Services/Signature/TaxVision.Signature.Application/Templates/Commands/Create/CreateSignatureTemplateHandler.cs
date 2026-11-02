using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Categories;
using TaxVision.Signature.Application.Limits;
using TaxVision.Signature.Domain.Settings;
using TaxVision.Signature.Domain.Templates;

namespace TaxVision.Signature.Application.Templates.Commands.Create;

public static class CreateSignatureTemplateHandler
{
    public static async Task<Result<SignatureTemplateResponse>> Handle(
        CreateSignatureTemplateCommand cmd,
        ISignatureTemplateRepository repository,
        ISignatureLimitsResolver limits,
        IUnitOfWork unitOfWork,
        ISignatureCategoryResolver categoryResolver,
        CancellationToken ct
    )
    {
        var category = await categoryResolver.ResolveAsync(cmd.TenantId, cmd.Category, ct);
        if (category.IsFailure)
            return Result.Failure<SignatureTemplateResponse>(category.Error);

        var policy = await limits.ResolveAsync(cmd.TenantId, ct);
        var factoryResult = CreateDraft(cmd, category.Value, policy.ForRequests);
        if (factoryResult.IsFailure)
            return Result.Failure<SignatureTemplateResponse>(factoryResult.Error);

        await PersistAsync(factoryResult.Value, repository, unitOfWork, ct);
        return Result.Success(SignatureTemplateResponse.From(factoryResult.Value));
    }

    private static Result<SignatureTemplate> CreateDraft(
        CreateSignatureTemplateCommand cmd,
        string category,
        SignatureLimits limits
    ) =>
        SignatureTemplate.CreateDraft(
            limits: limits,
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
            sendSealedDocumentToSigners: cmd.SendSealedDocumentToSigners,
            sendCertificateToSigners: cmd.SendCertificateToSigners,
            autoRemindersEnabled: cmd.AutoRemindersEnabled,
            reminderIntervalHours: cmd.ReminderIntervalHours,
            sendPartialCopyOnEachSignature: cmd.SendPartialCopyOnEachSignature,
            partialCopyAudienceKind: cmd.PartialCopyAudienceKind,
            partialCopyAudienceSlotOrders: cmd.PartialCopyAudienceSlotOrders,
            expirationEnabled: cmd.ExpirationEnabled
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
