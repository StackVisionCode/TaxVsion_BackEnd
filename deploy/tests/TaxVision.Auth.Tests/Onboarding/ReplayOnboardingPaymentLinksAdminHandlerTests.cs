using BuildingBlocks.Messaging.AuthIntegrationEvents;
using TaxVision.Auth.Onboarding.Application.Abstractions;
using TaxVision.Auth.Onboarding.Application.Admin.Commands;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Onboarding;

/// <summary>
/// Altas viejas cuyo pago quedó sin oficina ni recibo en PaymentApp: Auth vuelve a anunciar el vínculo
/// (alta → oficina → recibo) con un evento propio, sin tocar la saga ni los correos de bienvenida.
/// </summary>
public sealed class ReplayOnboardingPaymentLinksAdminHandlerTests
{
    [Fact]
    public async Task Each_paid_onboarding_is_announced_with_its_office_and_receipt()
    {
        var withReceipt = new OnboardingPaymentLink(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var withoutReceipt = new OnboardingPaymentLink(Guid.NewGuid(), Guid.NewGuid(), null);
        var repository = new FakeTenantOnboardingRepository { PaymentLinks = [withReceipt, withoutReceipt] };
        var bus = new FakeMessageBus();

        var result = await ReplayOnboardingPaymentLinksAdminHandler.Handle(
            new ReplayOnboardingPaymentLinksAdminCommand(),
            repository,
            bus,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value);
        var events = bus.Published.Cast<OnboardingPaymentLinkReplayedIntegrationEvent>().ToList();
        Assert.Collection(
            events,
            first =>
            {
                Assert.Equal(withReceipt.OnboardingId, first.OnboardingId);
                Assert.Equal(withReceipt.TenantId, first.CreatedTenantId);
                Assert.Equal(withReceipt.TenantId, first.TenantId);
                Assert.Equal(withReceipt.ReceiptFileId, first.ReceiptFileId);
            },
            second =>
            {
                Assert.Equal(withoutReceipt.OnboardingId, second.OnboardingId);
                Assert.Null(second.ReceiptFileId);
            }
        );
    }

    [Fact]
    public async Task Every_page_is_replayed()
    {
        var links = Enumerable
            .Range(0, ReplayOnboardingPaymentLinksAdminHandler.PageSize + 3)
            .Select(_ => new OnboardingPaymentLink(Guid.NewGuid(), Guid.NewGuid(), null))
            .ToList();
        var bus = new FakeMessageBus();

        var result = await ReplayOnboardingPaymentLinksAdminHandler.Handle(
            new ReplayOnboardingPaymentLinksAdminCommand(),
            new FakeTenantOnboardingRepository { PaymentLinks = links },
            bus,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.Equal(links.Count, result.Value);
        Assert.Equal(links.Count, bus.Published.Count);
    }

    [Fact]
    public async Task Nothing_to_replay_is_not_an_error()
    {
        var bus = new FakeMessageBus();

        var result = await ReplayOnboardingPaymentLinksAdminHandler.Handle(
            new ReplayOnboardingPaymentLinksAdminCommand(),
            new FakeTenantOnboardingRepository(),
            bus,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);
        Assert.Empty(bus.Published);
    }
}
