using BuildingBlocks.Domain;

namespace TaxVision.Subscription.Domain.Subscriptions;

/// <summary>
/// Descuento vigente de la suscripción. Como en Stripe, todo cobro cuya fecha cae dentro de [inicio, fin) lleva el
/// descuento: sirve igual para mensual y anual, y termina solo por fecha (no hace falta un job para cobrar bien).
/// "Once" cubre solo el cobro con fecha del día de inicio. Entidad hija de <see cref="TenantSubscription"/>: su
/// configuración EF requiere ValueGeneratedNever().
/// </summary>
public sealed class SubscriptionDiscount : BaseEntity
{
    public Guid TenantSubscriptionId { get; private set; }
    public Guid TenantId { get; private set; }
    public DiscountSource Source { get; private set; }
    public Guid? CodeDefinitionId { get; private set; }
    public int? PercentBasisPoints { get; private set; }
    public long? AmountCents { get; private set; }
    public string? Currency { get; private set; }
    public DiscountDuration Duration { get; private set; }
    public int? Months { get; private set; }
    public DiscountAppliesTo AppliesTo { get; private set; }

    /// <summary>Plan al que se limita (código restringido a un plan); null = cualquier plan.</summary>
    public string? PlanCode { get; private set; }

    public string? Reason { get; private set; }
    public DateTime StartsAtUtc { get; private set; }

    /// <summary>Código enmascarado con el que se canjeó (PREFIJO...ÚLTIMOS4); null si lo dio la plataforma.</summary>
    public string? CodeDisplay { get; private set; }

    /// <summary>Fin de la ventana (exclusivo); null = para siempre.</summary>
    public DateTime? EndsAtUtc { get; private set; }

    [UserReference(UserReferenceKind.Provenance)]
    public Guid CreatedBy { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? EndedAtUtc { get; private set; }
    public string? EndedReason { get; private set; }

    private SubscriptionDiscount() { }

    internal static SubscriptionDiscount Create(
        Guid tenantSubscriptionId,
        Guid tenantId,
        SubscriptionDiscountTerms terms,
        Guid actorUserId,
        DateTime nowUtc
    ) =>
        new()
        {
            TenantSubscriptionId = tenantSubscriptionId,
            TenantId = tenantId,
            Source = terms.Source,
            CodeDefinitionId = terms.CodeDefinitionId,
            PercentBasisPoints = terms.PercentBasisPoints,
            AmountCents = terms.AmountCents,
            Currency = terms.Currency,
            Duration = terms.Duration,
            Months = terms.Months,
            AppliesTo = terms.AppliesTo,
            PlanCode = terms.PlanCode,
            Reason = terms.Reason,
            StartsAtUtc = terms.StartsAtUtc,
            CodeDisplay = terms.CodeDisplay,
            EndsAtUtc = terms.Duration switch
            {
                DiscountDuration.Once => terms.StartsAtUtc.AddDays(1),
                DiscountDuration.Months => terms.StartsAtUtc.AddMonths(terms.Months!.Value),
                _ => null,
            },
            CreatedBy = actorUserId,
            CreatedAtUtc = nowUtc,
        };

    /// <summary>
    /// Cómo lo lee el cliente en el recibo: "Promo code SPRI...0420 · 20% off" o "Discount · $5.00 off". Nunca el
    /// código en claro.
    /// </summary>
    public string ReceiptLabel()
    {
        var name =
            Source == DiscountSource.Code
                ? CodeDisplay is null
                    ? "Promo code"
                    : $"Promo code {CodeDisplay}"
                : "Discount";
        var benefit =
            PercentBasisPoints is { } basisPoints
                ? $"{(basisPoints / 100m).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}%"
            : Currency == "USD"
                ? $"${(AmountCents!.Value / 100m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}"
            : $"{(AmountCents!.Value / 100m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} {Currency}";
        return $"{name} · {benefit} off";
    }

    /// <summary>Vigente: no terminado y su ventana no pasó (puede empezar en el futuro, p. ej. la próxima renovación).</summary>
    public bool IsActiveAt(DateTime nowUtc) => EndedAtUtc is null && (EndsAtUtc is null || nowUtc < EndsAtUtc);

    /// <summary>Descuento en centavos para un cobro; 0 si la fecha o el tipo de cobro no están cubiertos.</summary>
    /// <param name="chargePlanCode">Plan del cobro: el upgrade se mide contra el plan destino.</param>
    /// <param name="nextCharge">El cobro es "el próximo" aunque llegue antes de la renovación (el upgrade): un código de
    /// un solo cobro que espera la renovación lo cubre.</param>
    public long AmountFor(
        DiscountChargeKind kind,
        long grossCents,
        string currency,
        DateTime chargeDateUtc,
        string? chargePlanCode = null,
        bool nextCharge = false
    )
    {
        if (grossCents <= 0 || !Covers(kind, chargeDateUtc, chargePlanCode, nextCharge))
            return 0;

        if (PercentBasisPoints is { } basisPoints)
            return Math.Min(
                grossCents,
                (long)Math.Round(grossCents * basisPoints / 10_000m, MidpointRounding.AwayFromZero)
            );

        // Monto fijo: solo en su moneda y nunca más que el cobro (el sobrante no se acredita).
        return string.Equals(Currency, currency, StringComparison.OrdinalIgnoreCase)
            ? Math.Min(grossCents, AmountCents ?? 0)
            : 0;
    }

    internal void End(string reason, DateTime nowUtc)
    {
        EndedAtUtc = nowUtc;
        EndedReason = reason.Trim();
    }

    /// <summary>Un código de un solo cobro que todavía espera su cobro.</summary>
    public bool IsPendingOnce(DateTime nowUtc) =>
        Duration == DiscountDuration.Once && EndedAtUtc is null && (EndsAtUtc is null || nowUtc < EndsAtUtc);

    private bool Covers(DiscountChargeKind kind, DateTime chargeDateUtc, string? chargePlanCode, bool nextCharge) =>
        EndedAtUtc is null
        && (chargeDateUtc >= StartsAtUtc || (nextCharge && IsPendingOnce(chargeDateUtc)))
        && (EndsAtUtc is null || chargeDateUtc < EndsAtUtc)
        && (
            PlanCode is null
            || chargePlanCode is null
            || string.Equals(PlanCode, chargePlanCode, StringComparison.OrdinalIgnoreCase)
        )
        && (kind == DiscountChargeKind.Base || AppliesTo == DiscountAppliesTo.BaseSeatsAddOns);
}
