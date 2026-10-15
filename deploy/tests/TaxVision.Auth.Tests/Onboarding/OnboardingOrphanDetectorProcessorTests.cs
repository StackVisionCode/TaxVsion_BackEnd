using BuildingBlocks.Messaging.AuthIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Onboarding;
using TaxVision.Auth.Application.Onboarding.TenantOnboardings.Services;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Onboarding;

public sealed class OnboardingOrphanDetectorProcessorTests
{
    private static readonly DateTime Now = DateTime.UtcNow;
    private const int WindowHours = 24;

    [Fact]
    public async Task ProcessDueAsync_publishes_one_event_per_orphan_from_the_sweeper()
    {
        var orphan = OrphanOnboarding(paidAt: Now.AddHours(-26));
        var repo = new FakeTenantOnboardingRepository { Orphans = [orphan] };
        var bus = new FakeMessageBus();
        var processor = ProcessorFor(repo, bus);

        var published = await processor.ProcessDueAsync(Now, CancellationToken.None);

        Assert.Equal(1, published);
        var evt = Assert.Single(bus.Published.OfType<OnboardingOrphanDetectedIntegrationEvent>());
        Assert.Equal(orphan.Id, evt.OnboardingId);
        Assert.Equal(orphan.Email, evt.Email);
        Assert.Equal(Now.AddHours(-WindowHours), repo.LastOrphanCutoffUtc);
    }

    [Fact]
    public async Task ProcessDueAsync_is_a_noop_when_the_sweeper_returns_nothing()
    {
        var repo = new FakeTenantOnboardingRepository { Orphans = [] };
        var bus = new FakeMessageBus();
        var processor = ProcessorFor(repo, bus);

        var published = await processor.ProcessDueAsync(Now, CancellationToken.None);

        Assert.Equal(0, published);
        Assert.Empty(bus.Published);
    }

    private static OnboardingOrphanDetectorProcessor ProcessorFor(
        FakeTenantOnboardingRepository repo,
        FakeMessageBus bus
    ) =>
        new(
            repo,
            bus,
            new FakeCorrelationContext(),
            Options.Create(
                new OnboardingOrphanDetectorOptions
                {
                    Enabled = true,
                    IntervalMinutes = 60,
                    WindowHours = WindowHours,
                    BatchSize = 50,
                }
            ),
            NullLogger<OnboardingOrphanDetectorProcessor>.Instance
        );

    private static TenantOnboarding OrphanOnboarding(DateTime paidAt)
    {
        var onboarding = TenantOnboarding
            .Create(
                "orphan@example.com",
                paidAt.AddHours(-1),
                Guid.NewGuid(),
                "Smailin",
                "Buyer",
                null,
                paidAt.AddHours(-1)
            )
            .Value;
        onboarding.MarkPaymentProcessing(Guid.NewGuid(), "cs_test");
        onboarding.MarkPaymentCompleted("cs_test", paidAt);
        return onboarding;
    }
}
