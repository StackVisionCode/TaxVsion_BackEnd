using TaxVision.Codes.Domain.Definitions;

namespace TaxVision.Codes.Application.Definitions.CreateCodeDefinition;

public sealed record CreateCodeDefinitionResponse(
    Guid CodeDefinitionId,
    Guid OwnerTenantId,
    Guid? TenantScopeId,
    string Name,
    string Kind,
    string Status,
    string CodePrefix,
    string CodeLastFour,
    int RuleVersion,
    DateTime StartsAtUtc,
    DateTime? ExpiresAtUtc
)
{
    public static CreateCodeDefinitionResponse From(CodeDefinition definition) =>
        new(
            definition.Id,
            definition.TenantId,
            definition.TenantScopeId,
            definition.Name,
            definition.Kind.ToString(),
            definition.Status.ToString(),
            definition.Display.Prefix,
            definition.Display.LastFour,
            definition.RuleVersions.Max(rule => rule.Version),
            definition.StartsAtUtc,
            definition.ExpiresAtUtc
        );
}
