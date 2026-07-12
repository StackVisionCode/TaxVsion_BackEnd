using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Templates.ValueObjects;

namespace TaxVision.Signature.Application.Templates.Commands.AddSlot;

public static class AddTemplateSlotHandler
{
    public static async Task<Result<TemplateSlotCreatedResponse>> Handle(
        AddTemplateSlotCommand cmd,
        ISignatureTemplateRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var roleResult = TemplateSlotRole.Create(cmd.Role);
        if (roleResult.IsFailure)
            return Result.Failure<TemplateSlotCreatedResponse>(roleResult.Error);

        var template = await repository.GetByIdAsync(cmd.TenantId, cmd.TemplateId, ct);
        if (template is null)
            return Result.Failure<TemplateSlotCreatedResponse>(
                new Error("Signature.Template.NotFound", "The signature template does not exist for this tenant.")
            );

        var slotResult = template.AddSlot(roleResult.Value, cmd.DefaultLanguage);
        if (slotResult.IsFailure)
            return Result.Failure<TemplateSlotCreatedResponse>(slotResult.Error);

        await unitOfWork.SaveChangesAsync(ct);
        var slot = slotResult.Value;
        return Result.Success(
            new TemplateSlotCreatedResponse(slot.Id, slot.Order, slot.Role.Value, slot.DefaultLanguage)
        );
    }
}
