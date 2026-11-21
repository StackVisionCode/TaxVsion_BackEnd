using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Application.Trips;

/// <summary>Plazo del recorrido GPS de la oficina. Sin plazo propio se informa el que rige por defecto.</summary>
public sealed record MileageRetentionResponse(int? RetentionDays, bool Configured, int EffectiveRetentionDays)
{
    public static MileageRetentionResponse From(int? configuredDays) =>
        new(configuredDays, configuredDays is not null, MileageRetentionPolicy.EffectiveDays(configuredDays));
}
