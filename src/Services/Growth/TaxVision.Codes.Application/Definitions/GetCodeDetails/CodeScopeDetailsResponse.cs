using TaxVision.Codes.Domain.Definitions;

namespace TaxVision.Codes.Application.Definitions.GetCodeDetails;

public sealed record CodeScopeDetailsResponse(
    Guid ScopeId,
    string Type,
    string TargetId,
    string Mode
)
{
    public static CodeScopeDetailsResponse From(CodeScope scope) =>
        new(scope.Id, scope.Type.ToString(), scope.ScopeId, scope.Mode.ToString());
}
