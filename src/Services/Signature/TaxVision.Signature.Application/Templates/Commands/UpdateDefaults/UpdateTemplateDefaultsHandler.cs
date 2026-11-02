using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Limits;

namespace TaxVision.Signature.Application.Templates.Commands.UpdateDefaults;

public static class UpdateTemplateDefaultsHandler
{
    public static async Task<Result> Handle(
        UpdateTemplateDefaultsCommand cmd,
        ISignatureTemplateRepository repository,
        ISignatureLimitsResolver limits,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var template = await repository.GetByIdAsync(cmd.TenantId, cmd.TemplateId, ct);
        if (template is null)
            return Result.Failure(
                new Error("Signature.Template.NotFound", "The signature template does not exist for this tenant.")
            );

        var policy = await limits.ResolveAsync(cmd.TenantId, ct);
        var result = template.UpdateDefaults(
            policy.ForRequests,
            cmd.DefaultTokenExpirationHours,
            cmd.RequiresSequentialSigning,
            cmd.RequiresConsent,
            cmd.GenerateCertificate,
            cmd.SendSealedDocumentToSigners,
            cmd.SendCertificateToSigners,
            cmd.AutoRemindersEnabled,
            cmd.ReminderIntervalHours,
            cmd.SendPartialCopyOnEachSignature,
            cmd.PartialCopyAudienceKind,
            cmd.PartialCopyAudienceSlotOrders,
            cmd.ExpirationEnabled
        );
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
