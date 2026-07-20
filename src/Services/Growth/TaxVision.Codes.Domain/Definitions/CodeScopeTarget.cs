using BuildingBlocks.Results;

namespace TaxVision.Codes.Domain.Definitions;

public sealed record CodeScopeTarget
{
    public CodeScopeType Type { get; }
    public string ScopeId { get; }

    private CodeScopeTarget(CodeScopeType type, string scopeId)
    {
        Type = type;
        ScopeId = scopeId;
    }

    public static Result<CodeScopeTarget> Create(CodeScopeType type, string scopeId)
    {
        if (!Enum.IsDefined(type))
            return Result.Failure<CodeScopeTarget>(
                new Error("Codes.CodeScopeTarget.InvalidType", "Scope type is invalid.")
            );

        if (string.IsNullOrWhiteSpace(scopeId) || scopeId.Trim().Length > 200)
            return Result.Failure<CodeScopeTarget>(
                new Error("Codes.CodeScopeTarget.InvalidId", "ScopeId is required and cannot exceed 200 characters.")
            );

        return Result.Success(new CodeScopeTarget(type, scopeId.Trim()));
    }
}
