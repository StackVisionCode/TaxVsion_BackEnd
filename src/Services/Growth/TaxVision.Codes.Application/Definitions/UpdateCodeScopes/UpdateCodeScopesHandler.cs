using BuildingBlocks.Results;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Application.Common;
using TaxVision.Codes.Application.Definitions.GetCodeDetails;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Codes.Application.Definitions.UpdateCodeScopes;

public static class UpdateCodeScopesHandler
{
    public static async Task<Result<CodeDefinitionDetailsResponse>> Handle(
        UpdateCodeScopesCommand command,
        ICodeDefinitionRepository definitions,
        IBusinessIdempotencyExecutor idempotency,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        if (command.OwnerTenantId == Guid.Empty || command.CodeDefinitionId == Guid.Empty)
            return Failure(
                "Codes.UpdateCodeScopes.InvalidDefinition",
                "OwnerTenantId and CodeDefinitionId are required."
            );

        var key = IdempotencyKey.Create(command.IdempotencyKey);
        if (key.IsFailure)
            return Result.Failure<CodeDefinitionDetailsResponse>(key.Error);

        var scopes = (command.Scopes ?? [])
            .Select(scope => (scope.Type, scope.ScopeId ?? string.Empty, scope.Mode))
            .ToList();
        return await idempotency.ExecuteAsync(
            command.OwnerTenantId,
            "Codes.UpdateCodeScopes.v1",
            command.CodeDefinitionId,
            key.Value,
            Fingerprint(command),
            async operationCt =>
            {
                var definition = await definitions.GetOwnedByIdAsync(
                    command.OwnerTenantId,
                    command.CodeDefinitionId,
                    operationCt
                );
                if (definition is null || definition.TenantId != command.OwnerTenantId)
                    return Failure("Codes.UpdateCodeScopes.NotFound", "Owned code definition was not found.");

                var replaced = definition.ReplaceScopes(
                    scopes,
                    command.ActorUserId,
                    timeProvider.GetUtcNow().UtcDateTime
                );
                return replaced.IsFailure
                    ? Result.Failure<CodeDefinitionDetailsResponse>(replaced.Error)
                    : Result.Success(CodeDefinitionDetailsResponse.From(definition));
            },
            ct
        );
    }

    private static PayloadFingerprint Fingerprint(UpdateCodeScopesCommand command) =>
        OperationFingerprint.Create(
            command.OwnerTenantId,
            command.CodeDefinitionId,
            string.Join(
                ",",
                (command.Scopes ?? [])
                    .Select(scope => $"{(int)scope.Type}:{(scope.ScopeId ?? string.Empty).Trim()}:{(int)scope.Mode}")
                    .Order(StringComparer.Ordinal)
            ),
            command.ActorUserId
        );

    private static Result<CodeDefinitionDetailsResponse> Failure(string code, string message) =>
        Result.Failure<CodeDefinitionDetailsResponse>(new Error(code, message));
}
