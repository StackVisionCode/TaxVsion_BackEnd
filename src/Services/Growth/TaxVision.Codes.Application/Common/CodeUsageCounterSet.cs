using BuildingBlocks.Results;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Domain.Definitions;
using TaxVision.Codes.Domain.Usage;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Codes.Application.Common;

internal sealed class CodeUsageCounterSet
{
    private readonly CodeUsageCounter? _tenantCounter;
    private readonly CodeUsageCounter? _subjectCounter;

    private CodeUsageCounterSet(CodeUsageCounter? tenantCounter, CodeUsageCounter? subjectCounter)
    {
        _tenantCounter = tenantCounter;
        _subjectCounter = subjectCounter;
    }

    public static async Task<Result<CodeUsageCounterSet>> LoadAsync(
        CodeDefinition definition,
        Guid consumingTenantId,
        SubjectReference subject,
        ICodeUsageCounterRepository counters,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        CodeUsageCounter? tenantCounter = null;
        if (definition.MaxRedemptionsPerTenant is { } tenantLimit)
        {
            var keyResult = CodeUsageScopeKey.ForTenant(consumingTenantId);
            if (keyResult.IsFailure)
                return Result.Failure<CodeUsageCounterSet>(keyResult.Error);

            var counterResult = await counters.GetOrCreateForUpdateAsync(
                consumingTenantId,
                definition.Id,
                CodeUsageDimension.Tenant,
                keyResult.Value,
                tenantLimit,
                nowUtc,
                ct
            );
            if (counterResult.IsFailure)
                return Result.Failure<CodeUsageCounterSet>(counterResult.Error);

            var limitResult = counterResult.Value.EnsureLimit(tenantLimit);
            if (limitResult.IsFailure)
                return Result.Failure<CodeUsageCounterSet>(limitResult.Error);

            tenantCounter = counterResult.Value;
        }

        CodeUsageCounter? subjectCounter = null;
        if (definition.MaxRedemptionsPerSubject is { } subjectLimit)
        {
            var keyResult = CodeUsageScopeKey.ForSubject(subject);
            if (keyResult.IsFailure)
                return Result.Failure<CodeUsageCounterSet>(keyResult.Error);

            var counterResult = await counters.GetOrCreateForUpdateAsync(
                consumingTenantId,
                definition.Id,
                CodeUsageDimension.Subject,
                keyResult.Value,
                subjectLimit,
                nowUtc,
                ct
            );
            if (counterResult.IsFailure)
                return Result.Failure<CodeUsageCounterSet>(counterResult.Error);

            var limitResult = counterResult.Value.EnsureLimit(subjectLimit);
            if (limitResult.IsFailure)
                return Result.Failure<CodeUsageCounterSet>(limitResult.Error);

            subjectCounter = counterResult.Value;
        }

        return Result.Success(new CodeUsageCounterSet(tenantCounter, subjectCounter));
    }

    public Result ReserveAll(CodeDefinition definition, DateTime nowUtc)
    {
        var globalResult = definition.ReserveUse(nowUtc);
        if (globalResult.IsFailure)
            return globalResult;

        var tenantResult = _tenantCounter?.Reserve(nowUtc) ?? Result.Success();
        if (tenantResult.IsFailure)
            return tenantResult;

        return _subjectCounter?.Reserve(nowUtc) ?? Result.Success();
    }

    public Result CommitAll(CodeDefinition definition, bool availabilityWasReleased, DateTime nowUtc)
    {
        if (availabilityWasReleased)
        {
            var globalLateResult = definition.CommitLateUse(nowUtc);
            if (globalLateResult.IsFailure)
                return globalLateResult;

            var tenantLateResult = _tenantCounter?.CommitLate(nowUtc) ?? Result.Success();
            if (tenantLateResult.IsFailure)
                return tenantLateResult;

            return _subjectCounter?.CommitLate(nowUtc) ?? Result.Success();
        }

        var globalResult = definition.CommitReservedUse(nowUtc);
        if (globalResult.IsFailure)
            return globalResult;

        var tenantResult = _tenantCounter?.CommitReserved(nowUtc) ?? Result.Success();
        if (tenantResult.IsFailure)
            return tenantResult;

        return _subjectCounter?.CommitReserved(nowUtc) ?? Result.Success();
    }

    public Result ReleaseAll(CodeDefinition definition, DateTime nowUtc)
    {
        var globalResult = definition.ReleaseReservedUse(nowUtc);
        if (globalResult.IsFailure)
            return globalResult;

        var tenantResult = _tenantCounter?.ReleaseReserved(nowUtc) ?? Result.Success();
        if (tenantResult.IsFailure)
            return tenantResult;

        return _subjectCounter?.ReleaseReserved(nowUtc) ?? Result.Success();
    }

    public Result RestoreAll(CodeDefinition definition, DateTime nowUtc)
    {
        var globalResult = definition.RestoreCommittedUse(nowUtc);
        if (globalResult.IsFailure)
            return globalResult;

        var tenantResult = _tenantCounter?.RestoreCommitted(nowUtc) ?? Result.Success();
        if (tenantResult.IsFailure)
            return tenantResult;

        return _subjectCounter?.RestoreCommitted(nowUtc) ?? Result.Success();
    }
}
