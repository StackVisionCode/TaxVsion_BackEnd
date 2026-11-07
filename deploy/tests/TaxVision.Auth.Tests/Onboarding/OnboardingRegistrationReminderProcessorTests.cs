using BuildingBlocks.Messaging.AuthIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Onboarding;
using TaxVision.Auth.Application.Onboarding.TenantOnboardings.Services;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;
using TaxVision.Auth.Domain.Onboarding.ValueObjects;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Onboarding;

/// <summary>Que el marcado y el aviso se confirmen juntos lo mide OnboardingRegistrationReminderIntegrationTests.</summary>
public sealed class OnboardingRegistrationReminderProcessorTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public async Task PrepareDueAsync_builds_the_email_without_marking_when_reference_is_alive()
    {
        var store = new FakeTokenReferenceStore { ToPeek = "raw-token" };
        var onboarding = DueOnboarding(store.Reference);
        var repo = new FakeTenantOnboardingRepository { RemindersDue = [onboarding] };
        var processor = ProcessorFor(repo, store);

        var reminders = await processor.PrepareDueAsync(Now, CancellationToken.None);

        var evt = Assert.Single(reminders);
        Assert.Equal(onboarding.Id, evt.OnboardingId);
        Assert.Equal(onboarding.Email, evt.Email);
        Assert.Equal(store.Reference, evt.TokenReference);
        Assert.Null(onboarding.RegistrationEmailSentAtUtc);
    }

    [Fact]
    public async Task PrepareDueAsync_skips_the_onboarding_when_reference_expired_so_admin_can_recover()
    {
        // Guardrail: marcar RegistrationEmailSentAtUtc al perder la referencia convertía al cliente en
        // huérfano permanente — pagó pero nunca más recibía su link. El sweeper DEBE reintentar cada tick
        // y el admin repara con OnboardingAdmin.ResendRegistrationEmail regenerando un token nuevo.
        var store = new FakeTokenReferenceStore { ToPeek = null };
        var onboarding = DueOnboarding(store.Reference);
        var repo = new FakeTenantOnboardingRepository { RemindersDue = [onboarding] };
        var processor = ProcessorFor(repo, store);

        var reminders = await processor.PrepareDueAsync(Now, CancellationToken.None);

        Assert.Empty(reminders);
        Assert.Null(onboarding.RegistrationEmailSentAtUtc);
    }

    [Fact]
    public async Task PrepareDueAsync_returns_nothing_when_no_onboarding_is_due()
    {
        var store = new FakeTokenReferenceStore { ToPeek = "raw-token" };
        var processor = ProcessorFor(new FakeTenantOnboardingRepository { RemindersDue = [] }, store);

        Assert.Empty(await processor.PrepareDueAsync(Now, CancellationToken.None));
    }

    [Fact]
    public async Task MarkAndPublishAsync_marks_sent_and_publishes()
    {
        var store = new FakeTokenReferenceStore { ToPeek = "raw-token" };
        var onboarding = DueOnboarding(store.Reference);
        var repo = new FakeTenantOnboardingRepository { RemindersDue = [onboarding], Existing = onboarding };
        var processor = ProcessorFor(repo, store);
        var reminder = Assert.Single(await processor.PrepareDueAsync(Now, CancellationToken.None));
        var bus = new FakeMessageBus();

        var published = await processor.MarkAndPublishAsync(reminder, bus, Now, CancellationToken.None);

        Assert.True(published);
        Assert.Same(reminder, Assert.Single(bus.Published.OfType<OnboardingRegistrationReadyIntegrationEvent>()));
        Assert.NotNull(onboarding.RegistrationEmailSentAtUtc);
    }

    [Fact]
    public async Task MarkAndPublishAsync_does_not_resend_what_another_replica_already_sent()
    {
        var store = new FakeTokenReferenceStore { ToPeek = "raw-token" };
        var onboarding = DueOnboarding(store.Reference);
        var repo = new FakeTenantOnboardingRepository { RemindersDue = [onboarding], Existing = onboarding };
        var processor = ProcessorFor(repo, store);
        var reminder = Assert.Single(await processor.PrepareDueAsync(Now, CancellationToken.None));
        onboarding.MarkRegistrationEmailSent(Now);
        var bus = new FakeMessageBus();

        var published = await processor.MarkAndPublishAsync(reminder, bus, Now, CancellationToken.None);

        Assert.False(published);
        Assert.Empty(bus.Published);
    }

    private static OnboardingRegistrationReminderProcessor ProcessorFor(
        FakeTenantOnboardingRepository repo,
        FakeTokenReferenceStore store
    ) =>
        new(
            repo,
            store,
            new FakePlanCatalogClient("Pro"),
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
