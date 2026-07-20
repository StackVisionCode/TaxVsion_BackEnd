using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Codes.Domain.Usage;

public sealed class CodeUsageCounter : TenantEntity
{
    public Guid CodeDefinitionId { get; private set; }
    public CodeUsageDimension Dimension { get; private set; }
    public CodeUsageScopeKey ScopeKey { get; private set; } = null!;
    public long MaxRedemptions { get; private set; }
    public long ActiveReservations { get; private set; }
    public long CommittedRedemptions { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private CodeUsageCounter() { }

    public static Result<CodeUsageCounter> Create(
        Guid tenantId,
        Guid codeDefinitionId,
        CodeUsageDimension dimension,
        CodeUsageScopeKey scopeKey,
        long maxRedemptions,
        DateTime nowUtc
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<CodeUsageCounter>(
                new Error("Codes.CodeUsageCounter.InvalidTenant", "TenantId is required.")
            );

        if (codeDefinitionId == Guid.Empty)
            return Result.Failure<CodeUsageCounter>(
                new Error("Codes.CodeUsageCounter.InvalidCodeDefinition", "CodeDefinitionId is required.")
            );

        if (!Enum.IsDefined(dimension))
            return Result.Failure<CodeUsageCounter>(
                new Error("Codes.CodeUsageCounter.InvalidDimension", "Usage dimension is invalid.")
            );

        if (maxRedemptions <= 0)
            return Result.Failure<CodeUsageCounter>(
                new Error("Codes.CodeUsageCounter.InvalidLimit", "MaxRedemptions must be greater than zero.")
            );

        var counter = new CodeUsageCounter
        {
            CodeDefinitionId = codeDefinitionId,
            Dimension = dimension,
            ScopeKey = scopeKey,
            MaxRedemptions = maxRedemptions,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
        counter.SetTenant(tenantId);
        return Result.Success(counter);
    }

    public Result EnsureLimit(long expectedMaxRedemptions)
    {
        if (expectedMaxRedemptions != MaxRedemptions)
            return Result.Failure(
                new Error(
                    "Codes.CodeUsageCounter.LimitMismatch",
                    "The persisted usage limit does not match the immutable code definition limit."
                )
            );

        return Result.Success();
    }

    public Result Reserve(DateTime nowUtc)
    {
        if (
            CommittedRedemptions >= MaxRedemptions
            || ActiveReservations >= MaxRedemptions - CommittedRedemptions
        )
            return Result.Failure(
                new Error(
                    $"Codes.CodeUsageCounter.{Dimension}LimitReached",
                    $"The code has no remaining {Dimension.ToString().ToLowerInvariant()} availability."
                )
            );

        try
        {
            ActiveReservations = checked(ActiveReservations + 1);
        }
        catch (OverflowException)
        {
            return CounterOverflow();
        }

        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result CommitReserved(DateTime nowUtc)
    {
        if (ActiveReservations <= 0)
            return Result.Failure(
                new Error(
                    "Codes.CodeUsageCounter.NoActiveReservation",
                    $"No active {Dimension.ToString().ToLowerInvariant()} reservation is available to commit."
                )
            );

        try
        {
            ActiveReservations--;
            CommittedRedemptions = checked(CommittedRedemptions + 1);
        }
        catch (OverflowException)
        {
            ActiveReservations++;
            return CounterOverflow();
        }

        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result CommitLate(DateTime nowUtc)
    {
        try
        {
            CommittedRedemptions = checked(CommittedRedemptions + 1);
        }
        catch (OverflowException)
        {
            return CounterOverflow();
        }

        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result ReleaseReserved(DateTime nowUtc)
    {
        if (ActiveReservations <= 0)
            return Result.Failure(
                new Error(
                    "Codes.CodeUsageCounter.NoActiveReservation",
                    $"No active {Dimension.ToString().ToLowerInvariant()} reservation is available to release."
                )
            );

        ActiveReservations--;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result RestoreCommitted(DateTime nowUtc)
    {
        if (CommittedRedemptions <= 0)
            return Result.Failure(
                new Error(
                    "Codes.CodeUsageCounter.NoCommittedRedemption",
                    $"No committed {Dimension.ToString().ToLowerInvariant()} redemption is available to restore."
                )
            );

        CommittedRedemptions--;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    private static Result CounterOverflow() =>
        Result.Failure(
            new Error("Codes.CodeUsageCounter.CounterOverflow", "The usage counter overflowed.")
        );
}
