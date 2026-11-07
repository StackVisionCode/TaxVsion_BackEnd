using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentClient.Domain.PaymentLinks;
using TaxVision.PaymentClient.Domain.ValueObjects;
using PaymentLink = TaxVision.PaymentClient.Domain.PaymentLinks.PaymentLink;

namespace TaxVision.PaymentClient.Tests.Application;

/// <summary>Caracterización de PCL-11: qué ve el pagador con un link bloqueado o que ya no se puede canjear.</summary>
public sealed partial class RedeemPaymentLinkHandlerTests
{
    [Fact]
    public async Task Redeem_LinkBlockedByFailures_IsThrottled_WithoutCallingProvider()
    {
        var link = await SeedAsync();
        await ChangeAsync(
            link,
            stored =>
            {
                for (var i = 0; i < PaymentLinkAttemptPolicy.MaxRedemptionAttemptsPerLink; i++)
                    stored.MarkBlockedAfterExcessiveFailures(NowUtc);
            }
        );

        var result = await RedeemAsync(link, "card-token");

        Assert.Equal("PaymentLink.RedemptionThrottled", result.Error.Code);
        Assert.Equal("Too many failed redemption attempts; this link has been blocked.", result.Error.Message);
        Assert.Empty(_stripe.Requests);
    }

    [Fact]
    public async Task Redeem_RevokedLinkBelowTheLimit_IsNotFound()
    {
        var link = await SeedAsync();
        await ChangeAsync(
            link,
            stored =>
            {
                stored.MarkBlockedAfterExcessiveFailures(NowUtc);
                Assert.True(stored.Revoke("Invoice voided", NowUtc).IsSuccess);
            }
        );

        await AssertNotFoundAsync(link);
    }

    [Fact]
    public async Task Redeem_ExpiredStatusLink_IsNotFound()
    {
        var link = await SeedAsync();
        await ChangeAsync(link, stored => Assert.True(stored.Expire(NowUtc).IsSuccess));

        await AssertNotFoundAsync(link);
    }

    [Fact]
    public async Task Redeem_LinkPastItsExpiration_IsNotFound_EvenIfStillActive()
    {
        var link = await SeedAsync();
        await using (var db = InMemoryPaymentClient.CreateDb(_dbName))
        {
            var stored = await db.PaymentLinks.IgnoreQueryFilters().SingleAsync(l => l.Id == link.Id);
            db.Entry(stored).Property(l => l.ExpiresAtUtc).CurrentValue = NowUtc.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        await AssertNotFoundAsync(link);
    }

    [Fact]
    public async Task Redeem_UnknownToken_IsNotFound()
    {
        await SeedAsync();
        var unknown = PaymentLink
            .Create(
                _tenantId,
                null,
                Money.Create(2500, "USD").Value,
                PaymentPurpose.Create(PaymentPurposeKind.InvoicePayment, "inv-002").Value,
                PaymentLinkToken.Generate(),
                TimeSpan.FromDays(7),
                Guid.Empty,
                NowUtc
            )
            .Value;

        await AssertNotFoundAsync(unknown);
    }

    private async Task AssertNotFoundAsync(PaymentLink link)
    {
        var result = await RedeemAsync(link, "card-token");

        Assert.Equal("PaymentLink.NotFound", result.Error.Code);
        Assert.Equal("PaymentLink does not exist.", result.Error.Message);
        Assert.Empty(_stripe.Requests);
    }

    private async Task ChangeAsync(PaymentLink link, Action<PaymentLink> change)
    {
        await using var db = InMemoryPaymentClient.CreateDb(_dbName);
        change(await db.PaymentLinks.IgnoreQueryFilters().SingleAsync(l => l.Id == link.Id));
        await db.SaveChangesAsync();
    }
}
