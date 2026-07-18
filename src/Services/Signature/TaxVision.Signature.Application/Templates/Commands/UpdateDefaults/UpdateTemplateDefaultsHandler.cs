using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Application.Templates.Commands.UpdateDefaults;

public static class UpdateTemplateDefaultsHandler
{
    public static async Task<Result> Handle(
        UpdateTemplateDefaultsCommand cmd,
        ISignatureTemplateRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var template = await repository.GetByIdAsync(cmd.TenantId, cmd.TemplateId, ct);
        if (template is null)
            return Result.Failure(
                new Error("Signature.Template.NotFound", "The signature template does not exist for this tenant.")
            );

        var result = template.UpdateDefaults(
            cmd.DefaultTokenExpirationHours,
            cmd.RequiresSequentialSigning,
            cmd.RequiresConsent,
            cmd.GenerateCertificate
        );
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
