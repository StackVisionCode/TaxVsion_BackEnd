using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Application.Templates.Commands.RemoveSlot;

public static class RemoveTemplateSlotHandler
{
    public static async Task<Result> Handle(
        RemoveTemplateSlotCommand cmd,
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

        var result = template.RemoveSlot(cmd.SlotOrder);
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
