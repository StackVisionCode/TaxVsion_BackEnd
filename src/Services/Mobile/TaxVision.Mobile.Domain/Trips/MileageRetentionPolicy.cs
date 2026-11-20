using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Mobile.Domain.Trips;

/// <summary>
/// Plazo por oficina para conservar el recorrido GPS. Sin plazo configurado (sin fila o null) rige
/// <see cref="DefaultRetentionDays"/>: el recorrido nunca queda para siempre.
/// </summary>
public sealed class MileageRetentionPolicy : TenantEntity
{
    /// <summary>Tres años: el plazo típico de una auditoría fiscal.</summary>
    public const int DefaultRetentionDays = 1095;

    public const int MaxRetentionDays = 3650;

    private MileageRetentionPolicy() { }

    /// <summary>El plazo que se aplica: el configurado por la oficina o el por defecto.</summary>
    public static int EffectiveDays(int? configuredDays) => configuredDays ?? DefaultRetentionDays;

    public int? RetentionDays { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Result<MileageRetentionPolicy> Create(Guid tenantId, int? retentionDays, DateTime nowUtc)
    {
        if (tenantId == Guid.Empty || !IsValidPeriod(retentionDays))
            return Result.Failure<MileageRetentionPolicy>(TripErrors.InvalidRetention);
        var policy = new MileageRetentionPolicy
        {
            Id = tenantId,
            RetentionDays = retentionDays,
            UpdatedAtUtc = nowUtc,
        };
        policy.SetTenant(tenantId);
        return Result.Success(policy);
    }

    public Result Update(int? retentionDays, DateTime nowUtc)
    {
        if (!IsValidPeriod(retentionDays))
            return Result.Failure(TripErrors.InvalidRetention);
        RetentionDays = retentionDays;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    // Vacío es válido: rige el plazo por defecto.
    private static bool IsValidPeriod(int? days) => days is null or (>= 1 and <= MaxRetentionDays);
}
