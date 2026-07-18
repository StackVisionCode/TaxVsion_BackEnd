using BuildingBlocks.Results;

namespace TaxVision.PaymentClient.Domain.ValueObjects;

/// <summary>Cómo se reparte un cobro hecho vía <see cref="TenantPaymentConfigs.TenantPaymentMode.Connect"/>
/// entre el tenant y la plataforma. El invariante — <c>TenantAmountCents + PlatformFeeAmountCents ==
/// Amount</c> — lo hace cumplir <c>TenantPayment.MarkProcessingViaConnect</c>, no este VO (acá
/// no se conoce el monto total del pago).</summary>
public sealed record SplitPaymentBreakdown
{
    public long TenantAmountCents { get; }
    public long PlatformFeeAmountCents { get; }
    public string? PlatformFeeReference { get; }

    private SplitPaymentBreakdown(long tenantAmountCents, long platformFeeAmountCents, string? platformFeeReference)
    {
        TenantAmountCents = tenantAmountCents;
        PlatformFeeAmountCents = platformFeeAmountCents;
        PlatformFeeReference = platformFeeReference;
    }

    public static Result<SplitPaymentBreakdown> Create(
        long tenantAmountCents,
        long platformFeeAmountCents,
        string? platformFeeReference
    )
    {
        if (tenantAmountCents < 0)
            return Result.Failure<SplitPaymentBreakdown>(
                new Error("SplitPaymentBreakdown.NegativeTenantAmount", "TenantAmountCents cannot be negative.")
            );

        if (platformFeeAmountCents < 0)
            return Result.Failure<SplitPaymentBreakdown>(
                new Error("SplitPaymentBreakdown.NegativePlatformFee", "PlatformFeeAmountCents cannot be negative.")
            );

        return Result.Success(
            new SplitPaymentBreakdown(tenantAmountCents, platformFeeAmountCents, platformFeeReference?.Trim())
        );
    }
}
