using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Templates.ValueObjects;

namespace TaxVision.Signature.Application.Templates.Commands.UpdateSlot;

public sealed record UpdateTemplateSlotCommand(
    Guid TenantId,
    Guid TemplateId,
    int SlotOrder,
    string Role,
    string DefaultLanguage,
    SignerVerificationMethod? RequiredVerificationMethod
);

public static class UpdateTemplateSlotHandler
{
    public static async Task<Result> Handle(
        UpdateTemplateSlotCommand cmd,
        ISignatureTemplateRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var roleResult = TemplateSlotRole.Create(cmd.Role);
        if (roleResult.IsFailure)
            return Result.Failure(roleResult.Error);

        var template = await repository.GetByIdAsync(cmd.TenantId, cmd.TemplateId, ct);
        if (template is null)
            return Result.Failure(
                new Error("Signature.Template.NotFound", "The signature template does not exist for this tenant.")
            );

        var result = template.UpdateSlot(
            cmd.SlotOrder,
            roleResult.Value,
            cmd.DefaultLanguage,
            cmd.RequiredVerificationMethod
        );
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
