using BuildingBlocks.Results;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Application.Common;
using TaxVision.Codes.Application.Definitions.Common;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Codes.Application.Definitions.ActivateCode;

public static class ActivateCodeHandler
{
    public static async Task<Result<CodeDefinitionStateResponse>> Handle(
        ActivateCodeCommand command,
        ICodeDefinitionRepository definitions,
        IBusinessIdempotencyExecutor idempotency,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        var validationResult = Validate(command);
        if (validationResult.IsFailure)
            return Result.Failure<CodeDefinitionStateResponse>(validationResult.Error);

        var keyResult = IdempotencyKey.Create(command.IdempotencyKey);
        if (keyResult.IsFailure)
            return Result.Failure<CodeDefinitionStateResponse>(keyResult.Error);

        var fingerprint = OperationFingerprint.Create(
            command.OwnerTenantId,
            command.CodeDefinitionId,
            command.ActorUserId
        );
        return await idempotency.ExecuteAsync(
            "Codes.ActivateCode.v1",
            command.CodeDefinitionId,
            keyResult.Value,
            fingerprint,
            async operationCt =>
            {
                var definition = await definitions.GetOwnedByIdAsync(
                    command.OwnerTenantId,
                    command.CodeDefinitionId,
                    operationCt
                );
                if (definition is null || definition.TenantId != command.OwnerTenantId)
                    return Failure("Codes.ActivateCode.NotFound", "Owned code definition was not found.");

                var activateResult = definition.Activate(command.ActorUserId, timeProvider.GetUtcNow().UtcDateTime);
                return activateResult.IsFailure
                    ? Result.Failure<CodeDefinitionStateResponse>(activateResult.Error)
                    : Result.Success(CodeDefinitionStateResponse.From(definition));
            },
            ct
        );
    }

    private static Result Validate(ActivateCodeCommand command)
    {
        if (command.OwnerTenantId == Guid.Empty)
            return Result.Failure(new Error("Codes.ActivateCode.InvalidOwner", "OwnerTenantId is required."));

        if (command.CodeDefinitionId == Guid.Empty)
            return Result.Failure(new Error("Codes.ActivateCode.InvalidDefinition", "CodeDefinitionId is required."));

        if (command.ActorUserId == Guid.Empty)
            return Result.Failure(new Error("Codes.ActivateCode.InvalidActor", "ActorUserId is required."));

        return Result.Success();
    }

    private static Result<CodeDefinitionStateResponse> Failure(string code, string message) =>
        Result.Failure<CodeDefinitionStateResponse>(new Error(code, message));
}
