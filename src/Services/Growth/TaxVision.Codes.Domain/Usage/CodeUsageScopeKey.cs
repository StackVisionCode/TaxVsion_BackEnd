using BuildingBlocks.Results;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Codes.Domain.Usage;

public sealed record CodeUsageScopeKey
{
    public string Value { get; }

    private CodeUsageScopeKey(string value) => Value = value;

    public static Result<CodeUsageScopeKey> ForTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<CodeUsageScopeKey>(
                new Error("Codes.CodeUsageScopeKey.InvalidTenant", "TenantId is required.")
            );

        return Result.Success(new CodeUsageScopeKey(tenantId.ToString("N")));
    }

    public static Result<CodeUsageScopeKey> ForSubject(SubjectReference subject)
    {
        var value = $"{(int)subject.Type}:{subject.SubjectId}";
        if (value.Length > 250)
            return Result.Failure<CodeUsageScopeKey>(
                new Error(
                    "Codes.CodeUsageScopeKey.SubjectTooLong",
                    "The canonical subject usage key cannot exceed 250 characters."
                )
            );

        return Result.Success(new CodeUsageScopeKey(value));
    }

    public static Result<CodeUsageScopeKey> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 250)
            return Result.Failure<CodeUsageScopeKey>(
                new Error(
                    "Codes.CodeUsageScopeKey.Invalid",
                    "Usage scope key is required and cannot exceed 250 characters."
                )
            );

        return Result.Success(new CodeUsageScopeKey(value.Trim()));
    }

    public override string ToString() => Value;
}
