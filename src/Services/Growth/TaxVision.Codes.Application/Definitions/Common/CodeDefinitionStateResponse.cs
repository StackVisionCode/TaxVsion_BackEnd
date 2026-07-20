using TaxVision.Codes.Domain.Definitions;

namespace TaxVision.Codes.Application.Definitions.Common;

public sealed record CodeDefinitionStateResponse(
    Guid CodeDefinitionId,
    Guid OwnerTenantId,
    string Status,
    string CodePrefix,
    string CodeLastFour,
    DateTime UpdatedAtUtc
)
{
    public static CodeDefinitionStateResponse From(CodeDefinition definition) =>
        new(
            definition.Id,
            definition.TenantId,
            definition.Status.ToString(),
            definition.Display.Prefix,
            definition.Display.LastFour,
            definition.UpdatedAtUtc
        );
}
