using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Limits;
using TaxVision.Signature.Application.Platform;
using TaxVision.Signature.Domain.Templates.ValueObjects;

namespace TaxVision.Signature.Application.Templates.Commands.AddSlot;

public static class AddTemplateSlotHandler
{
    public static async Task<Result<TemplateSlotCreatedResponse>> Handle(
        AddTemplateSlotCommand cmd,
        ISignatureTemplateRepository repository,
        ISignatureLimitsResolver limits,
        ISignaturePlatformSettingsProvider platform,
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

        var policy = await limits.ResolveAsync(cmd.TenantId, ct);
        var maxRoles = (await platform.GetAsync(ct)).MaxRolesPerTemplate;
        var slotResult = template.AddSlot(
            policy.ForRequests,
            maxRoles,
            roleResult.Value,
            cmd.DefaultLanguage,
            cmd.RequiredVerificationMethod
        );
        if (slotResult.IsFailure)
            return Result.Failure<TemplateSlotCreatedResponse>(slotResult.Error);

        await unitOfWork.SaveChangesAsync(ct);
        var slot = slotResult.Value;
        return Result.Success(
            new TemplateSlotCreatedResponse(
                slot.Id,
                slot.Order,
                slot.Role.Value,
                slot.DefaultLanguage,
                slot.RequiredVerificationMethod
            )
        );
    }
}
