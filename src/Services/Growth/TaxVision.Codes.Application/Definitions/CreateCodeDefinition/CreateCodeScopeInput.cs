using TaxVision.Codes.Domain.Definitions;

namespace TaxVision.Codes.Application.Definitions.CreateCodeDefinition;

public sealed record CreateCodeScopeInput(
    CodeScopeType Type,
    string ScopeId,
    CodeScopeMode Mode
);
