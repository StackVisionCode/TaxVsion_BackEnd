using BuildingBlocks.Messaging.AuthIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Onboarding;
using TaxVision.Auth.Application.Onboarding.TenantOnboardings.Services;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;
using TaxVision.Auth.Domain.Onboarding.ValueObjects;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Onboarding;

public sealed class OnboardingRegistrationReminderProcessorTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public async Task ProcessDueAsync_publishes_the_email_and_marks_sent_when_reference_is_alive()
    {
        var store = new FakeTokenReferenceStore { ToPeek = "raw-token" };
        var onboarding = DueOnboarding(store.Reference);
        var repo = new FakeTenantOnboardingRepository { RemindersDue = [onboarding] };
        var bus = new FakeMessageBus();
        var unitOfWork = new FakeUnitOfWork();
        var processor = ProcessorFor(repo, store, bus, unitOfWork);

        var published = await processor.ProcessDueAsync(Now, CancellationToken.None);

        Assert.Equal(1, published);
        var evt = Assert.Single(bus.Published.OfType<OnboardingRegistrationReadyIntegrationEvent>());
        Assert.Equal(onboarding.Id, evt.OnboardingId);
        Assert.Equal(onboarding.Email, evt.Email);
        Assert.Equal(store.Reference, evt.TokenReference);
        Assert.NotNull(onboarding.RegistrationEmailSentAtUtc);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task ProcessDueAsync_does_not_mark_sent_when_reference_expired_so_admin_can_recover()
    {
        // Guardrail: marcar RegistrationEmailSentAtUtc al perder la referencia convertía al cliente en
        // huérfano permanente — pagó pero nunca más recibía su link. El sweeper DEBE reintentar cada tick
        // y el admin repara con OnboardingAdmin.ResendRegistrationEmail regenerando un token nuevo.
        var store = new FakeTokenReferenceStore { ToPeek = null };
        var onboarding = DueOnboarding(store.Reference);
        var repo = new FakeTenantOnboardingRepository { RemindersDue = [onboarding] };
        var bus = new FakeMessageBus();
        var unitOfWork = new FakeUnitOfWork();
        var processor = ProcessorFor(repo, store, bus, unitOfWork);

        var published = await processor.ProcessDueAsync(Now, CancellationToken.None);

        Assert.Equal(0, published);
        Assert.Empty(bus.Published.OfType<OnboardingRegistrationReadyIntegrationEvent>());
        Assert.Null(onboarding.RegistrationEmailSentAtUtc);
        // SaveChanges se sigue llamando al final del batch (es un no-op sin tracked changes); lo que
        // importa es que el aggregate NO quedó marcado como sent.
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task ProcessDueAsync_does_nothing_when_no_onboarding_is_due()
    {
        var store = new FakeTokenReferenceStore { ToPeek = "raw-token" };
        var repo = new FakeTenantOnboardingRepository { RemindersDue = [] };
        var bus = new FakeMessageBus();
        var unitOfWork = new FakeUnitOfWork();
        var processor = ProcessorFor(repo, store, bus, unitOfWork);

        var published = await processor.ProcessDueAsync(Now, CancellationToken.None);

        Assert.Equal(0, published);
        Assert.Empty(bus.Published);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    private static OnboardingRegistrationReminderProcessor ProcessorFor(
        FakeTenantOnboardingRepository repo,
        FakeTokenReferenceStore store,
        FakeMessageBus bus,
        FakeUnitOfWork unitOfWork
    ) =>
        new(
            repo,
            store,
            new FakePlanCatalogClient("Pro"),
            unitOfWork,
            bus,
            Options.Create(new OnboardingOptions { RegistrationReminderDelayMinutes = 45 }),
            NullLogger<OnboardingRegistrationReminderProcessor>.Instance
        );

    private static TenantOnboarding DueOnboarding(Guid tokenReference)
    {
        var onboarding = TenantOnboarding
            .Create("buyer@example.com", Now, Guid.NewGuid(), "Ada", "Lovelace", null, Now)
            .Value;
        onboarding.MarkPaymentProcessing(Guid.NewGuid(), "cs_test");
        onboarding.MarkPaymentCompleted("cs_test", Now.AddMinutes(-60));
        onboarding.RecordSettledAmount(4900, "USD");
        onboarding.SetRegistrationToken(
            RegistrationTokenHash.Create(new string('a', 64)).Value,
            Now.AddHours(72),
            tokenReference
        );
        return onboarding;
    }
}
