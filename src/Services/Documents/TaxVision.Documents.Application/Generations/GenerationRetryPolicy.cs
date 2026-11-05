using BuildingBlocks.Results;
using TaxVision.Documents.Domain.Generations;
using TaxVision.Documents.Domain.ValueObjects;

namespace TaxVision.Documents.Application.Generations;

/// <summary>Umbral para dar por atascada una generación a medias.</summary>
public sealed class GenerationRetryOptions
{
    public const string SectionName = "Documents:GenerationRetry";

    public TimeSpan StalledAfter { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>
/// Regla común de reintento con la misma clave: lo fallido o lo atascado se reencola; lo terminado y lo
/// que está en curso reciente se devuelve tal cual (una entrega repetida no duplica renders).
/// </summary>
public static class GenerationRetryPolicy
{
    public static bool IsRetryable(DocumentGeneration generation, DateTime nowUtc, TimeSpan stalledAfter) =>
        generation.Status == DocumentGenerationStatus.Failed
        || (
            generation.Status != DocumentGenerationStatus.Completed
            && generation.Status != DocumentGenerationStatus.Cancelled
            && nowUtc - generation.UpdatedAtUtc >= stalledAfter
        );

    public static Result Requeue(DocumentGeneration generation, DateTime nowUtc) =>
        generation.Status == DocumentGenerationStatus.Failed
            ? generation.RetryFromFailure(nowUtc)
            : generation.RetryStalled(nowUtc);
}
