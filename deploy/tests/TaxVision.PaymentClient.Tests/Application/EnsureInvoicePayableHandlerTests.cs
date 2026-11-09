using TaxVision.PaymentClient.Application.Payables.EnsureInvoicePayable;
using TaxVision.PaymentClient.Domain.Payables;
using TaxVision.PaymentClient.Domain.PaymentLinks;
using TaxVision.PaymentClient.Domain.ValueObjects;
using static TaxVision.PaymentClient.Tests.Application.ResolvePayableHandlerTests;

namespace TaxVision.PaymentClient.Tests.Application;

/// <summary>Billing cambia el monto de la factura: el link ya acuñado deja de cobrar el monto viejo.</summary>
public sealed class EnsureInvoicePayableHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private const string InvoiceId = "0b6f3c1e-2a4d-4e5f-9a7b-1c2d3e4f5a6b";

    private static PayableReference CreatePayable(long amountCents) =>
        PayableReference
            .Create(
                TenantId,
                PaymentPurposeKind.InvoicePayment,
                InvoiceId,
                Money.Create(amountCents, "USD").Value,
                DateTime.UtcNow
            )
            .Value;

    private static PaymentLink CreateLink(long amountCents) =>
        PaymentLink
            .Create(
                TenantId,
                null,
                Money.Create(amountCents, "USD").Value,
                PaymentPurpose.Create(PaymentPurposeKind.InvoicePayment, InvoiceId).Value,
                PaymentLinkToken.Generate(),
                TimeSpan.FromDays(7),
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;

    [Fact]
    public async Task Ensure_WhenAmountChanged_ExpiresActiveLink()
    {
        var payable = CreatePayable(15000L);
        var link = CreateLink(15000L);

        var result = await Ensure(payable, link, new FakePayments(), amountCents: 5000L);

        Assert.True(result.IsSuccess);
        Assert.Equal(5000L, payable.Amount.AmountCents);
        Assert.NotNull(payable.AmountUpdatedAtUtc);
        Assert.Equal(PaymentLinkStatus.Expired, link.Status);
    }

    [Fact]
    public async Task Ensure_WhenAmountChangedWithAttemptInProgress_KeepsActiveLink()
    {
        // Un 3DS en curso sobre ese link: vencerlo dejaría el cobro sin link al que volver.
        var payable = CreatePayable(15000L);
        var link = CreateLink(15000L);

        await Ensure(payable, link, new FakePayments { HasAttemptInProgress = true }, amountCents: 5000L);

        Assert.Equal(PaymentLinkStatus.Active, link.Status);
    }

    [Fact]
    public async Task Ensure_WhenSettledAndAmountPositive_Reopens()
    {
        // INV-2026-00002: estaba pagada, se editó al alza y Billing manda el saldo de 150000.
        var payable = CreatePayable(300000L);
        payable.Settle(DateTime.UtcNow.AddHours(-1));

        var result = await Ensure(payable, null, new FakePayments(), amountCents: 150000L);

        Assert.True(result.IsSuccess);
        Assert.False(payable.IsSettled);
        Assert.Equal(150000L, payable.Amount.AmountCents);
        Assert.NotNull(payable.AmountUpdatedAtUtc);
    }

    [Fact]
    public async Task Ensure_WhenSettledSameAmount_StillReopens()
    {
        var payable = CreatePayable(150000L);
        payable.Settle(DateTime.UtcNow.AddHours(-1));

        await Ensure(payable, null, new FakePayments(), amountCents: 150000L);

        Assert.False(payable.IsSettled);
        Assert.NotNull(payable.AmountUpdatedAtUtc);
    }

    [Fact]
    public async Task Ensure_WhenRevoked_DoesNotReopen()
    {
        var payable = CreatePayable(150000L);
        payable.Settle(DateTime.UtcNow.AddHours(-2));
        payable.Revoke(DateTime.UtcNow.AddHours(-1));

        await Ensure(payable, null, new FakePayments(), amountCents: 150000L);

        Assert.True(payable.IsRevoked);
        Assert.True(payable.IsSettled);
    }

    private static Task<BuildingBlocks.Results.Result<EnsureInvoicePayableResponse>> Ensure(
        PayableReference payable,
        PaymentLink? link,
        FakePayments payments,
        long amountCents
    ) =>
        EnsureInvoicePayableHandler.Handle(
            new EnsureInvoicePayableCommand(TenantId, amountCents, "USD", InvoiceId),
            new FakePayables(payable),
            new FakeLinks { Active = link },
            payments,
            new FakeTenants(),
            new NoOpUnitOfWork(),
            CancellationToken.None
        );
}
