using BuildingBlocks.Results;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Billing.Tests.Invoices;

/// <summary>
/// Billing = verdad financiera del onboarding. <see cref="Invoice.CreateForOnboarding"/> asienta una
/// factura en TODOS los casos comerciales: pago normal (Paid), parcial con código (Mixed), y cubierto
/// 100% ($0, sin PaymentId, FullyCoveredByCode) — con una línea de ajuste por beneficio. Cubre las
/// invariantes financieras (net = gross − discount, Σajustes = descuento, pago solo si net &gt; 0).
/// </summary>
public sealed class OnboardingInvoiceFactoryTests
{
    private static readonly DateTime Now = DateTime.UtcNow;
    private static readonly Guid Tenant = Guid.Parse("8f58a521-4c25-4d91-9f4e-7ad5df14c001");

    private static CustomerSnapshot Customer() =>
        new(Guid.NewGuid(), "Wagner Alcantara", "buyer@example.com", null, null, null);

    private static IssuerSnapshot Issuer() =>
        new(
            "TaxVision",
            new Address("1 Main", null, "City", "ST", "00000", "US"),
            null,
            "billing@taxvision.local",
            null,
            null
        );

    private static Result<Invoice> Create(
        long gross,
        long discount,
        long net,
        Guid? paymentId,
        SettlementType settlement,
        IReadOnlyList<OnboardingInvoiceAdjustment> adjustments
    ) =>
        Invoice.CreateForOnboarding(
            Tenant,
            onboardingId: Guid.NewGuid(),
            planId: Guid.NewGuid(),
            paymentId,
            invoiceNumber: "ONB-2026-00001",
            Customer(),
            Issuer(),
            planDescription: "Enterprise (Monthly)",
            grossAmountCents: gross,
            discountAmountCents: discount,
            netAmountCents: net,
            currency: "USD",
            settlementType: settlement,
            adjustments,
            nowUtc: Now,
            dueDateUtc: Now
        );

    [Fact]
    public void FullyCovered_creates_zero_invoice_without_payment_and_two_adjustment_lines()
    {
        var result = Create(
            gross: 12900,
            discount: 12900,
            net: 0,
            paymentId: null,
            SettlementType.FullyCoveredByCode,
            adjustments:
            [
                new OnboardingInvoiceAdjustment(InvoiceAdjustmentType.Referral, "WELCOME20", Guid.NewGuid(), 2580),
                new OnboardingInvoiceAdjustment(InvoiceAdjustmentType.Gift, "WELCOME100", Guid.NewGuid(), 10320),
            ]
        );

        Assert.True(result.IsSuccess);
        var inv = result.Value;
        Assert.Equal(SettlementType.FullyCoveredByCode, inv.SettlementType);
        Assert.Null(inv.PaymentId);
        Assert.Equal(12900, inv.Subtotal.AmountCents);
        Assert.Equal(12900, inv.DiscountTotal.AmountCents);
        Assert.Equal(0, inv.Total.AmountCents);
        Assert.Equal(2, inv.Adjustments.Count);
        Assert.Equal(12900, inv.Adjustments.Sum(a => a.Amount.AmountCents));
        Assert.Contains(inv.Adjustments, a => a.Type == InvoiceAdjustmentType.Referral && a.Code == "WELCOME20");
        Assert.Contains(inv.Adjustments, a => a.Type == InvoiceAdjustmentType.Gift && a.Code == "WELCOME100");
    }

    [Fact]
    public void Mixed_creates_invoice_with_payment_and_adjustment()
    {
        var payment = Guid.NewGuid();

        var result = Create(
            gross: 12900,
            discount: 2580,
            net: 10320,
            paymentId: payment,
            SettlementType.Mixed,
            adjustments:
            [
                new OnboardingInvoiceAdjustment(InvoiceAdjustmentType.Referral, "WELCOME20", Guid.NewGuid(), 2580),
            ]
        );

        Assert.True(result.IsSuccess);
        var inv = result.Value;
        Assert.Equal(SettlementType.Mixed, inv.SettlementType);
        Assert.Equal(payment, inv.PaymentId);
        Assert.Equal(10320, inv.Total.AmountCents);
        Assert.Single(inv.Adjustments);
    }

    [Fact]
    public void Paid_no_codes_creates_full_price_invoice_without_adjustments()
    {
        var payment = Guid.NewGuid();

        var result = Create(12900, 0, 12900, payment, SettlementType.Paid, adjustments: []);

        Assert.True(result.IsSuccess);
        var inv = result.Value;
        Assert.Equal(SettlementType.Paid, inv.SettlementType);
        Assert.Equal(payment, inv.PaymentId);
        Assert.Equal(0, inv.DiscountTotal.AmountCents);
        Assert.Equal(12900, inv.Total.AmountCents);
        Assert.Empty(inv.Adjustments);
    }

    [Fact]
    public void Rejects_positive_net_without_payment()
    {
        var result = Create(
            12900,
            2580,
            10320,
            paymentId: null,
            SettlementType.Mixed,
            adjustments: [new OnboardingInvoiceAdjustment(InvoiceAdjustmentType.Referral, "WELCOME20", null, 2580)]
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Billing.Invoice.PaymentRequired", result.Error.Code);
    }

    [Fact]
    public void Rejects_fully_covered_with_payment()
    {
        var result = Create(
            12900,
            12900,
            0,
            paymentId: Guid.NewGuid(),
            SettlementType.FullyCoveredByCode,
            adjustments: [new OnboardingInvoiceAdjustment(InvoiceAdjustmentType.Gift, "WELCOME100", null, 12900)]
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Billing.Invoice.UnexpectedPayment", result.Error.Code);
    }

    [Fact]
    public void Rejects_adjustment_sum_mismatch()
    {
        var result = Create(
            12900,
            2580,
            10320,
            paymentId: Guid.NewGuid(),
            SettlementType.Mixed,
            // La suma de ajustes (2000) no coincide con el descuento (2580).
            adjustments: [new OnboardingInvoiceAdjustment(InvoiceAdjustmentType.Referral, "WELCOME20", null, 2000)]
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Billing.Invoice.AdjustmentMismatch", result.Error.Code);
    }

    [Fact]
    public void Rejects_discount_exceeding_gross()
    {
        var result = Create(
            10000,
            12000,
            0,
            paymentId: null,
            SettlementType.FullyCoveredByCode,
            adjustments: [new OnboardingInvoiceAdjustment(InvoiceAdjustmentType.Gift, "TOOBIG", null, 12000)]
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Billing.Invoice.DiscountExceedsGross", result.Error.Code);
    }

    [Fact]
    public void Rejects_net_not_equal_gross_minus_discount()
    {
        var result = Create(
            12900,
            2000,
            9999,
            paymentId: Guid.NewGuid(),
            SettlementType.Mixed,
            adjustments: [new OnboardingInvoiceAdjustment(InvoiceAdjustmentType.Promo, "P", null, 2000)]
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Billing.Invoice.InvalidNet", result.Error.Code);
    }
}
