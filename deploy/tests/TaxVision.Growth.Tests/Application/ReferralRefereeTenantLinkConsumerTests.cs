using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.GrowthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Growth.Api.IntegrationEvents;
using TaxVision.Growth.Tests.Application.Fakes;
using TaxVision.Growth.Tests.Domain;
using TaxVision.Referrals.Application.Attributions.LinkReferralRefereeTenant;
using TaxVision.Referrals.Application.Fraud.ScreenReferral;
using TaxVision.Referrals.Application.Rewards;
using TaxVision.Referrals.Application.Rewards.GetReferralRewardNotice;
using TaxVision.Referrals.Domain.Fraud;

namespace TaxVision.Growth.Tests.Application;

/// <summary>
/// Al completarse el alta, su atribución de referido (que guarda el alta como referido) queda enlazada con la oficina
/// real que se creó. Repetir el evento no cambia nada y un alta sin referido se ignora.
/// </summary>
public sealed class ReferralRefereeTenantLinkConsumerTests
{
    private static readonly Guid Office = Guid.NewGuid();

    [Fact]
    public async Task Completed_onboarding_links_its_attribution()
    {
        var onboardingId = Guid.NewGuid();
        var attribution = AttributionFor(onboardingId);
        var unitOfWork = new CountingUnitOfWork();

        var result = await HandleAsync(
            new InMemoryReferralAttributionRepository(attribution),
            unitOfWork,
            onboardingId
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(Office, attribution.RefereeTenantId);
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task Replay_is_idempotent()
    {
        var onboardingId = Guid.NewGuid();
        var attribution = AttributionFor(onboardingId);
        var attributions = new InMemoryReferralAttributionRepository(attribution);

        Assert.True((await HandleAsync(attributions, new CountingUnitOfWork(), onboardingId)).IsSuccess);
        var replay = await HandleAsync(attributions, new CountingUnitOfWork(), onboardingId);

        Assert.True(replay.IsSuccess);
        Assert.Equal(Office, attribution.RefereeTenantId);
    }

    [Fact]
    public async Task Onboarding_without_referral_is_ignored()
    {
        var unitOfWork = new CountingUnitOfWork();

        var result = await HandleAsync(new InMemoryReferralAttributionRepository(), unitOfWork, Guid.NewGuid());

        Assert.True(result.IsSuccess);
        Assert.Equal(0, unitOfWork.Saves);
    }

    [Fact]
    public async Task The_completed_event_links_and_screens_under_the_onboarding_tenant()
    {
        var onboardingId = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var bus = new ScriptedBus(message =>
            message is ScreenReferralCommand ? Result.Success(ReferralScreeningResult.Clear) : Result.Success()
        );

        await HandleCompletedAsync(bus, onboardingId, owner);

        var command = Assert.IsType<LinkReferralRefereeTenantCommand>(bus.Invoked[0]);
        Assert.Equal(onboardingId, command.OnboardingId);
        Assert.Equal(Office, command.RefereeTenantId);
        Assert.Equal(owner, command.RefereeUserId);
        Assert.Equal(onboardingId, Assert.IsType<ScreenReferralCommand>(bus.Invoked[1]).OnboardingId);
        // La atribución es del alta: los comandos corren con ese tenant y el del bus se restaura.
        Assert.All(bus.TenantIdDuring, tenant => Assert.Equal(onboardingId.ToString(), tenant));
        Assert.Null(bus.TenantId);
        Assert.Empty(bus.Published);
    }

    /// <summary>
    /// El aviso de premio cancelado por autorreferido lo devuelve el screening en su transacción: el consumer ya no lo
    /// busca ni lo publica.
    /// </summary>
    [Fact]
    public async Task Rejected_notice_is_published_in_the_same_unit()
    {
        var onboardingId = Guid.NewGuid();
        var referrer = Guid.NewGuid();
        var rewardCaseId = Guid.NewGuid();
        var notice = new ReferralRewardNotice(
            referrer,
            rewardCaseId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            5_000,
            "USD",
            GrowthTestData.NowUtc
        );
        var bus = new ScriptedBus(message =>
            message switch
            {
                ScreenReferralCommand => Result.Success(
                    new ReferralScreeningResult(
                        ReferralFraudVerdict.Block,
                        ReferralFraudScreen.SameUser,
                        rewardCaseId,
                        referrer,
                        RewardCancelled: true
                    )
                ),
                GetReferralRewardNoticeQuery => Result.Success<ReferralRewardNotice?>(notice),
                _ => Result.Success(),
            }
        );

        await HandleCompletedAsync(bus, onboardingId, Guid.NewGuid());

        Assert.Equal("corr-screen", Assert.IsType<ScreenReferralCommand>(bus.Invoked[1]).CorrelationId);
        Assert.Empty(bus.Published);
        Assert.DoesNotContain(bus.Invoked, message => message is GetReferralRewardNoticeQuery);
        Assert.Null(bus.TenantId);
    }

    private static Task HandleCompletedAsync(ScriptedBus bus, Guid onboardingId, Guid owner) =>
        ReferralRefereeTenantLinkConsumer.Handle(
            new TenantOnboardingCompletedIntegrationEvent
            {
                TenantId = Office,
                OnboardingId = onboardingId,
                CompletedTenantId = Office,
                CompletedUserId = owner,
                CompletedSubscriptionId = Guid.NewGuid(),
                CorrelationId = "corr-screen",
            },
            bus,
            NullLogger<TenantOnboardingCompletedIntegrationEvent>.Instance,
            default
        );

    private static TaxVision.Referrals.Domain.Attributions.ReferralAttribution AttributionFor(Guid onboardingId)
    {
        var program = GrowthTestData.CreateActiveTenantReferralProgram();
        return GrowthTestData.CreateActiveAttribution(
            program,
            GrowthTestData.CreateReferralCode(program),
            onboardingId
        );
    }

    private static Task<Result> HandleAsync(
        InMemoryReferralAttributionRepository attributions,
        IUnitOfWork unitOfWork,
        Guid onboardingId
    ) =>
        LinkReferralRefereeTenantHandler.Handle(
            new LinkReferralRefereeTenantCommand(onboardingId, Office),
            attributions,
            unitOfWork,
            new FixedTimeProvider(new DateTimeOffset(GrowthTestData.NowUtc)),
            CancellationToken.None
        );

    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int Saves { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            Saves++;
            return Task.FromResult(1);
        }
    }
}
