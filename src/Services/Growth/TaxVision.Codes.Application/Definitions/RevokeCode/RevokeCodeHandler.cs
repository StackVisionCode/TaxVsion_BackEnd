using BuildingBlocks.Results;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Application.Common;
using TaxVision.Codes.Application.Definitions.Common;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Codes.Application.Definitions.RevokeCode;

public static class RevokeCodeHandler
{
    public static async Task<Result<CodeDefinitionStateResponse>> Handle(
        RevokeCodeCommand command,
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
            "Codes.RevokeCode.v1",
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
                    return Failure("Codes.RevokeCode.NotFound", "Owned code definition was not found.");

                var revokeResult = definition.Revoke(command.ActorUserId, timeProvider.GetUtcNow().UtcDateTime);
                return revokeResult.IsFailure
                    ? Result.Failure<CodeDefinitionStateResponse>(revokeResult.Error)
                    : Result.Success(CodeDefinitionStateResponse.From(definition));
            },
            ct
        );
    }

    private static Result Validate(RevokeCodeCommand command)
    {
        if (command.OwnerTenantId == Guid.Empty)
            return Result.Failure(new Error("Codes.RevokeCode.InvalidOwner", "OwnerTenantId is required."));

        if (command.CodeDefinitionId == Guid.Empty)
            return Result.Failure(new Error("Codes.RevokeCode.InvalidDefinition", "CodeDefinitionId is required."));

        if (command.ActorUserId == Guid.Empty)
            return Result.Failure(new Error("Codes.RevokeCode.InvalidActor", "ActorUserId is required."));

        return Result.Success();
    }

    private static Result<CodeDefinitionStateResponse> Failure(string code, string message) =>
        Result.Failure<CodeDefinitionStateResponse>(new Error(code, message));
}
