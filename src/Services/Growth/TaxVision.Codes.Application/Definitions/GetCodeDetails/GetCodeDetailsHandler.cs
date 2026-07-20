using BuildingBlocks.Results;
using TaxVision.Codes.Application.Abstractions;

namespace TaxVision.Codes.Application.Definitions.GetCodeDetails;

public static class GetCodeDetailsHandler
{
    public static async Task<Result<CodeDefinitionDetailsResponse>> Handle(
        GetCodeDetailsQuery query,
        ICodeDefinitionRepository definitions,
        CancellationToken ct
    )
    {
        if (query.OwnerTenantId == Guid.Empty)
            return Failure("Codes.GetCodeDetails.InvalidOwner", "OwnerTenantId is required.");

        if (query.CodeDefinitionId == Guid.Empty)
            return Failure("Codes.GetCodeDetails.InvalidDefinition", "CodeDefinitionId is required.");

        if (query.ActorUserId == Guid.Empty)
            return Failure("Codes.GetCodeDetails.InvalidActor", "ActorUserId is required.");

        var definition = await definitions.GetOwnedByIdAsync(
            query.OwnerTenantId,
            query.CodeDefinitionId,
            ct
        );
        if (definition is null || definition.TenantId != query.OwnerTenantId)
            return Failure("Codes.GetCodeDetails.NotFound", "Owned code definition was not found.");

        return Result.Success(CodeDefinitionDetailsResponse.From(definition));
    }

    private static Result<CodeDefinitionDetailsResponse> Failure(string code, string message) =>
        Result.Failure<CodeDefinitionDetailsResponse>(new Error(code, message));
}
