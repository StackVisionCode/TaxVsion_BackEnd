using TaxVision.Billing.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Billing.Tests;

/// <summary>SCAFFOLD B1: smoke test que confirma que el dominio compila y los VOs básicos operan.
/// La suite real (transiciones de estado, numeración, recibos) llega con las fases B2/B3.</summary>
public sealed class ScaffoldSmokeTests
{
    [Fact]
    public void Money_Create_Rejects_Negative()
    {
        var result = Money.Create(-1, "USD");
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Money_Create_Normalizes_Currency()
    {
        var result = Money.Create(1000, "usd");
        Assert.True(result.IsSuccess);
        Assert.Equal("USD", result.Value.Currency);
        Assert.Equal(1000, result.Value.AmountCents);
    }

    [Fact]
    public void InvoiceNumber_Create_Rejects_Empty()
    {
        var result = InvoiceNumber.Create("  ");
        Assert.True(result.IsFailure);
    }
}
