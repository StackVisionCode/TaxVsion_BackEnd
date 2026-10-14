using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Api.Jobs;
using TaxVision.Auth.Application.Onboarding;
using TaxVision.Auth.Application.Onboarding.TenantOnboardings.Services;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Onboarding;

public sealed class OnboardingExpirationProcessorTests
{
    private static readonly DateTime Now = DateTime.UtcNow;
    private const int TtlMinutes = 30;

    [Fact]
    public async Task ProcessDueAsync_marks_expired_the_onboardings_returned_by_the_sweeper()
    {
        var old = OnboardingAt(Now.AddMinutes(-45));
        var repo = new FakeTenantOnboardingRepository { ExpiredPending = [old] };
        var unitOfWork = new FakeUnitOfWork();
        var processor = ProcessorFor(repo, unitOfWork);

        var expired = await processor.ProcessDueAsync(Now, CancellationToken.None);

        Assert.Equal(1, expired);
        Assert.Equal(TenantOnboardingStatus.Expired, old.Status);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
        Assert.Equal(Now.AddMinutes(-TtlMinutes), repo.LastExpiredPendingCutoffUtc);
    }

    [Fact]
    public async Task ProcessDueAsync_is_a_noop_when_nothing_is_due()
    {
        var repo = new FakeTenantOnboardingRepository { ExpiredPending = [] };
        var unitOfWork = new FakeUnitOfWork();
        var processor = ProcessorFor(repo, unitOfWork);

        var expired = await processor.ProcessDueAsync(Now, CancellationToken.None);

        Assert.Equal(0, expired);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public void MarkExpired_is_idempotent_on_an_already_expired_onboarding()
    {
        var onboarding = OnboardingAt(Now.AddMinutes(-45));
        Assert.True(onboarding.MarkExpired().IsSuccess);

        var again = onboarding.MarkExpired();

        Assert.True(again.IsSuccess);
        Assert.Equal(TenantOnboardingStatus.Expired, onboarding.Status);
    }

    [Fact]
    public async Task OnboardingExpirationJob_does_not_touch_the_scope_factory_when_the_flag_is_disabled()
    {
        var throwingScope = new ThrowingScopeFactory();
        var job = new OnboardingExpirationJob(
            throwingScope,
            new FakeLifetime(),
            Options.Create(
                new OnboardingExpirationOptions
                {
                    Enabled = false,
                    TtlMinutes = TtlMinutes,
                    IntervalMinutes = 5,
                    BatchSize = 100,
                }
            ),
            NullLogger<OnboardingExpirationJob>.Instance
        );

        await job.StartAsync(CancellationToken.None);
        await job.StopAsync(CancellationToken.None);

        Assert.False(throwingScope.WasCalled);
    }

    private static OnboardingExpirationProcessor ProcessorFor(
        FakeTenantOnboardingRepository repo,
        FakeUnitOfWork unitOfWork
    ) =>
        new(
            repo,
            unitOfWork,
            Options.Create(
                new OnboardingExpirationOptions
                {
                    Enabled = true,
                    TtlMinutes = TtlMinutes,
                    IntervalMinutes = 5,
                    BatchSize = 100,
                }
            ),
            NullLogger<OnboardingExpirationProcessor>.Instance
        );

    private static TenantOnboarding OnboardingAt(DateTime createdAtUtc) =>
        TenantOnboarding
            .Create("buyer@example.com", createdAtUtc, Guid.NewGuid(), "Ada", "Lovelace", null, createdAtUtc)
            .Value;

    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted { get; } = new(canceled: true);
        public CancellationToken ApplicationStopping { get; } = CancellationToken.None;
        public CancellationToken ApplicationStopped { get; } = CancellationToken.None;

        public void StopApplication() { }
    }

    private sealed class ThrowingScopeFactory : IServiceScopeFactory
    {
        public bool WasCalled { get; private set; }

        public IServiceScope CreateScope()
        {
            WasCalled = true;
            throw new InvalidOperationException("Scope factory must not be touched when the job is disabled.");
        }
    }
}
