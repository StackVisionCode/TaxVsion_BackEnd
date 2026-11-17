using BuildingBlocks.Results;
using TaxVision.Codes.Application.Abstractions;

namespace TaxVision.Codes.Application.Definitions.ExpireCode;

public static class ExpireCodeHandler
{
    public static async Task<Result> Handle(
        ExpireCodeCommand command,
        ICodeDefinitionRepository definitions,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        var definition = await definitions.GetOwnedByIdAsync(command.OwnerTenantId, command.CodeDefinitionId, ct);
        if (definition is null)
            return Result.Failure(new Error("Codes.ExpireCode.NotFound", "Owned code definition was not found."));

        // Si ya no toca (otro barrido lo cerró o cambió la fecha), el dominio lo rechaza y no se guarda nada.
        return definition.Expire(timeProvider.GetUtcNow().UtcDateTime);
    }
}
