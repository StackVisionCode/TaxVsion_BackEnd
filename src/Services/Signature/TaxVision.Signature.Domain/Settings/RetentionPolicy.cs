using BuildingBlocks.Results;

namespace TaxVision.Signature.Domain.Settings;

/// <summary>
/// Política de retención de solicitudes de firma completadas. Value Object inmutable.
/// El default (7 años) coincide con la retención IRS para documentos fiscales; los
/// tenants con regulaciones más largas pueden ampliar.
/// </summary>
public sealed record RetentionPolicy
{
    public const int DefaultRetentionYears = 7;
    public const int MinRetentionYears = 1;
    public const int MaxRetentionYears = 20;

    public int RetentionYears { get; private init; }
    public bool AllowPurge { get; private init; }

    private RetentionPolicy() { }

    public static RetentionPolicy Default() => new() { RetentionYears = DefaultRetentionYears, AllowPurge = false };

    public Result<RetentionPolicy> WithYears(int years)
    {
        if (years is < MinRetentionYears or > MaxRetentionYears)
            return Result.Failure<RetentionPolicy>(
                new Error("Signature.Retention.Years", "RetentionYears must be between 1 and 20.")
            );

        return Result.Success(this with { RetentionYears = years });
    }

    public RetentionPolicy WithPurgeAllowed() => this with { AllowPurge = true };

    public RetentionPolicy WithPurgeBlocked() => this with { AllowPurge = false };
}
