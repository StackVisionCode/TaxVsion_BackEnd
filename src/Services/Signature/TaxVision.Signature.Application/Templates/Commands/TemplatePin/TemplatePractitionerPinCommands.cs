using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Application.Templates.Commands.TemplatePin;

/// <summary>Fija el Practitioner PIN por defecto de la plantilla (Form 8879). El valor en claro solo
/// cruza la capa Application hasta el hasher; se guarda el hash PBKDF2.</summary>
public sealed record SetTemplatePractitionerPinCommand(Guid TenantId, Guid TemplateId, string Pin);

/// <summary>Quita el Practitioner PIN por defecto de la plantilla.</summary>
public sealed record ClearTemplatePractitionerPinCommand(Guid TenantId, Guid TemplateId);

public static class SetTemplatePractitionerPinHandler
{
    public static async Task<Result> Handle(
        SetTemplatePractitionerPinCommand cmd,
        ISignatureTemplateRepository repository,
        IPinHasher hasher,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var pinResult = PractitionerPin.Create(cmd.Pin);
        if (pinResult.IsFailure)
            return pinResult;

        var template = await repository.GetByIdAsync(cmd.TenantId, cmd.TemplateId, ct);
        if (template is null)
            return Result.Failure(
                new Error("Signature.Template.NotFound", "The signature template does not exist for this tenant.")
            );

        var setResult = template.SetPractitionerPin(hasher.Hash(pinResult.Value.Value));
        if (setResult.IsFailure)
            return setResult;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public static class ClearTemplatePractitionerPinHandler
{
    public static async Task<Result> Handle(
        ClearTemplatePractitionerPinCommand cmd,
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

        var clearResult = template.ClearPractitionerPin();
        if (clearResult.IsFailure)
            return clearResult;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
