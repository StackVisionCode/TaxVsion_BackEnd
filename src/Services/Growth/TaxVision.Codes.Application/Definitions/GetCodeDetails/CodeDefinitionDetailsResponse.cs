using TaxVision.Codes.Domain.Definitions;

namespace TaxVision.Codes.Application.Definitions.GetCodeDetails;

public sealed record CodeDefinitionDetailsResponse(
    Guid CodeDefinitionId,
    Guid OwnerTenantId,
    string OwnerScope,
    Guid? TenantScopeId,
    string Name,
    string Kind,
    string Status,
    string CodePrefix,
    string CodeLastFour,
    DateTime StartsAtUtc,
    DateTime? ExpiresAtUtc,
    long? MaxRedemptions,
    long? MaxRedemptionsPerTenant,
    long? MaxRedemptionsPerSubject,
    long ActiveReservations,
    long CommittedRedemptions,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyCollection<CodeRuleDetailsResponse> Rules,
    IReadOnlyCollection<CodeScopeDetailsResponse> Scopes
)
{
    public static CodeDefinitionDetailsResponse From(CodeDefinition definition) =>
        new(
            definition.Id,
            definition.TenantId,
            definition.OwnerScope.ToString(),
            definition.TenantScopeId,
            definition.Name,
            definition.Kind.ToString(),
            definition.Status.ToString(),
            definition.Display.Prefix,
            definition.Display.LastFour,
            definition.StartsAtUtc,
            definition.ExpiresAtUtc,
            definition.MaxRedemptions,
            definition.MaxRedemptionsPerTenant,
            definition.MaxRedemptionsPerSubject,
            definition.ActiveReservations,
            definition.CommittedRedemptions,
            definition.CreatedAtUtc,
            definition.UpdatedAtUtc,
            definition.RuleVersions.OrderBy(rule => rule.Version).Select(CodeRuleDetailsResponse.From).ToArray(),
            definition.Scopes.Select(CodeScopeDetailsResponse.From).ToArray()
        );
}
