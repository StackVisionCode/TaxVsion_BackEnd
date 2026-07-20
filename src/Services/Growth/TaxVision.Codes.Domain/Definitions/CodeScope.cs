using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Codes.Domain.Definitions;

public sealed class CodeScope : TenantEntity
{
    public Guid CodeDefinitionId { get; private set; }
    public CodeScopeType Type { get; private set; }
    public string ScopeId { get; private set; } = default!;
    public CodeScopeMode Mode { get; private set; }

    private CodeScope() { }

    internal static Result<CodeScope> Create(
        Guid tenantId,
        Guid codeDefinitionId,
        CodeScopeType type,
        string scopeId,
        CodeScopeMode mode
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<CodeScope>(
                new Error("Codes.CodeScope.InvalidTenant", "TenantId is required.")
            );

        if (codeDefinitionId == Guid.Empty)
            return Result.Failure<CodeScope>(
                new Error("Codes.CodeScope.InvalidCodeDefinition", "CodeDefinitionId is required.")
            );

        if (!Enum.IsDefined(type))
            return Result.Failure<CodeScope>(new Error("Codes.CodeScope.InvalidType", "Scope type is invalid."));

        if (!Enum.IsDefined(mode))
            return Result.Failure<CodeScope>(new Error("Codes.CodeScope.InvalidMode", "Scope mode is invalid."));

        if (string.IsNullOrWhiteSpace(scopeId) || scopeId.Trim().Length > 200)
            return Result.Failure<CodeScope>(
                new Error("Codes.CodeScope.InvalidId", "ScopeId is required and cannot exceed 200 characters.")
            );

        var scope = new CodeScope
            {
                CodeDefinitionId = codeDefinitionId,
                Type = type,
                ScopeId = scopeId.Trim(),
                Mode = mode,
            };
        scope.SetTenant(tenantId);
        return Result.Success(scope);
    }

    public bool Matches(CodeScopeTarget target) =>
        Type == target.Type && string.Equals(ScopeId, target.ScopeId, StringComparison.Ordinal);
}
