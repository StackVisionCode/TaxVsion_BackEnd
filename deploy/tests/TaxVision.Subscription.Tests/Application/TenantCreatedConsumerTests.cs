using BuildingBlocks.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

// El plan por defecto todavía no publicado es un prerrequisito que llega tarde: se reintenta con esperas largas.
public sealed class TenantCreatedConsumerTests
{
    [Fact]
    public async Task DefaultPlanUnpublished_ThrowsDependencyNotReady()
    {
        // AddAsync del doble lanza NotSupported: si llegara a crear la suscripción, la excepción sería otra.
        var subscriptions = new FakeSubscriptionRepo(null);
        var unitOfWork = new FakeUnitOfWork();
        var bus = new CapturingMessageBus();

        await Assert.ThrowsAsync<DependencyNotReadyException>(() =>
            TenantCreatedConsumer.Handle(
                new TenantCreatedIntegrationEvent
                {
                    NewTenantId = Guid.NewGuid(),
                    Name = "Acme",
                    SubDomain = "acme",
                    AdminEmail = "admin@example.test",
                    AdminInvitationTokenHash = "hash",
                },
                subscriptions,
                settingsRepository: null!,
                new FakePlanRepo(null),
                Options.Create(new SubscriptionOptions()),
                unitOfWork,
                bus,
                new FakeCorrelationContext(),
                NullLogger<TenantSubscription>.Instance,
                CancellationToken.None
            )
        );

        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
        Assert.Empty(bus.Published);
    }
}
