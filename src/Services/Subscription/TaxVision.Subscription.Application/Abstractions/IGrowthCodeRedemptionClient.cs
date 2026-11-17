using BuildingBlocks.Results;

namespace TaxVision.Subscription.Application.Abstractions;

/// <summary>Lo que Subscription le pide a Growth para cotizar un código contra la base de una oficina.</summary>
public sealed record GrowthCodeQuoteRequest(
    Guid TenantId,
    string Code,
    Guid PlanId,
    long GrossAmountCents,
    string Currency,
    string SnapshotHash,
    Guid RedemptionId,
    // Ciclo del plan (Monthly | Yearly): Growth limita códigos por plan y por plan + ciclo.
    string? BillingCycle = null
);

/// <summary>El beneficio que da el código y cuánto dura (Once | Months | Forever).</summary>
public sealed record GrowthCodeQuote(
    Guid QuoteId,
    Guid CodeDefinitionId,
    int? PercentageBasisPoints,
    long? FixedAmountCents,
    string Currency,
    string Duration,
    int? DurationMonths,
    // Código enmascarado (PREFIJO...ÚLTIMOS4) para el recibo.
    string? CodeDisplay = null
);

/// <summary>
/// Canje de un código por una oficina que ya tiene suscripción: cotizar, reservar, confirmar y, si algo falla
/// después de reservar, cancelar. Cada paso es idempotente por <c>RedemptionId</c>.
/// </summary>
public interface IGrowthCodeRedemptionClient
{
    Task<Result<GrowthCodeQuote>> QuoteAsync(GrowthCodeQuoteRequest request, CancellationToken ct);

    Task<Result<Guid>> ReserveAsync(Guid tenantId, Guid quoteId, Guid redemptionId, CancellationToken ct);

    Task<Result> CommitAsync(
        Guid tenantId,
        Guid reservationId,
        Guid redemptionId,
        string snapshotHash,
        CancellationToken ct
    );

    Task<Result> CancelAsync(Guid tenantId, Guid reservationId, Guid redemptionId, string reason, CancellationToken ct);
}
