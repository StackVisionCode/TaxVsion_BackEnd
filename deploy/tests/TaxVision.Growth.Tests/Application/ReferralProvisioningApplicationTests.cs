using System.Text.Json;
using BuildingBlocks.Tenancy;
using TaxVision.Growth.Tests.Application.Fakes;
using TaxVision.Growth.Tests.Domain;
using TaxVision.Referrals.Application.Codes.IssueTenantReferralCode;
using TaxVision.Referrals.Application.Programs.ActivateTenantReferralProgram;
using TaxVision.Referrals.Application.Programs.CreateTenantReferralProgram;
using TaxVision.Referrals.Domain.Codes;
using TaxVision.Referrals.Domain.Programs;
using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Growth.Tests.Application;

public sealed class ReferralProvisioningApplicationTests
{
    private const string GeneratedToken = "TVR-00112233445566778899AABBCCDDEEFF";

    [Fact]
    public async Task Create_program_applies_defaults_and_replays_exact_metadata()
    {
        var programs = new InMemoryReferralProgramRepository();
        var idempotency = new FakeReferralIdempotencyExecutor(programs);
        var timeProvider = new FixedTimeProvider(new DateTimeOffset(GrowthTestData.NowUtc));
        var command = new CreateTenantReferralProgramCommand(
            "partner-2026",
            "Partner referrals",
            GrowthTestData.NowUtc,
            GrowthTestData.NowUtc.AddYears(1),
            Policy: null,
            GrowthTestData.ActorId,
            "create-partner-2026"
        );

        var first = await CreateTenantReferralProgramHandler.Handle(
            command,
            programs,
            idempotency,
            timeProvider,
            CancellationToken.None
        );
        var replay = await CreateTenantReferralProgramHandler.Handle(
            command,
            programs,
            idempotency,
            timeProvider,
            CancellationToken.None
        );

        Assert.True(first.IsSuccess);
        Assert.Equal(first.Value, replay.Value);
        Assert.Equal("PARTNER-2026", first.Value.ProgramCode);
        Assert.Equal(ReferralProgramScope.Platform, first.Value.Scope);
        Assert.Equal(ReferralFlowType.TenantToTenant, first.Value.Flow);
        Assert.Equal(ReferralProgramStatus.Draft, first.Value.Status);
        Assert.Equal(ReferralProgramPolicy.DefaultAttributionWindowDays, first.Value.Policy.AttributionWindowDays);
        Assert.Equal(ReferralProgramPolicy.DefaultWaitingPeriodDays, first.Value.Policy.WaitingPeriodDays);
        Assert.Equal(ReferralRewardType.SubscriptionFeatureGrant, first.Value.Policy.RewardType);
        Assert.Single(programs.Items);
        Assert.Equal(PlatformTenant.Id, programs.Items.Single().TenantId);
        Assert.Equal(1, idempotency.ExecutedBodyCount);
    }

    [Fact]
    public async Task Create_program_conflicts_when_the_same_key_changes_explicit_policy()
    {
        var programs = new InMemoryReferralProgramRepository();
        var idempotency = new FakeReferralIdempotencyExecutor(programs);
        var timeProvider = new FixedTimeProvider(new DateTimeOffset(GrowthTestData.NowUtc));
        var command = new CreateTenantReferralProgramCommand(
            "PARTNER-EXPLICIT",
            "Explicit partner referrals",
            GrowthTestData.NowUtc,
            GrowthTestData.NowUtc.AddYears(1),
            new TenantReferralProgramPolicyInput(
                AttributionWindowDays: 60,
                MinimumPaymentAmountCents: 5_000,
                MinimumPaymentCurrency: "usd",
                WaitingPeriodDays: 45,
                MaximumRewardsPerReferrerPerCalendarYear: 8,
                RewardType: ReferralRewardType.SubscriptionTrialExtension,
                RewardDefinitionKey: "referrals.explicit"
            ),
            GrowthTestData.ActorId,
            "create-explicit"
        );

        var first = await CreateTenantReferralProgramHandler.Handle(
            command,
            programs,
            idempotency,
            timeProvider,
            CancellationToken.None
        );
        var conflict = await CreateTenantReferralProgramHandler.Handle(
            command with
            {
                Policy = command.Policy! with { WaitingPeriodDays = 46 },
            },
            programs,
            idempotency,
            timeProvider,
            CancellationToken.None
        );

        Assert.True(first.IsSuccess);
        Assert.Equal("USD", first.Value.Policy.MinimumPaymentCurrency);
        Assert.True(conflict.IsFailure);
        Assert.Equal("Referrals.IdempotencyConflict", conflict.Error.Code);
        Assert.Single(programs.Items);
        Assert.Equal(1, idempotency.ExecutedBodyCount);
    }

    [Fact]
    public async Task Activate_program_replays_without_reapplying_the_transition()
    {
        var program = ReferralProgram
            .Create(
                "ACTIVATE-2026",
                "Activation test",
                ReferralProgramScope.Platform,
                tenantScopeId: null,
                ReferralFlowType.TenantToTenant,
                ReferralProgramPolicy.TenantToTenantDefaults(),
                GrowthTestData.NowUtc,
                GrowthTestData.NowUtc.AddYears(1),
                "seed-program",
                GrowthTestData.Sha('a'),
                GrowthTestData.ActorId,
                GrowthTestData.NowUtc
            )
            .Value;
        var programs = new InMemoryReferralProgramRepository(program);
        var idempotency = new FakeReferralIdempotencyExecutor();
        var command = new ActivateTenantReferralProgramCommand(program.Id, GrowthTestData.ActorId, "activate-program");
        var timeProvider = new FixedTimeProvider(new DateTimeOffset(GrowthTestData.NowUtc));

        var first = await ActivateTenantReferralProgramHandler.Handle(
            command,
            programs,
            idempotency,
            timeProvider,
            CancellationToken.None
        );
        var replay = await ActivateTenantReferralProgramHandler.Handle(
            command,
            programs,
            idempotency,
            timeProvider,
            CancellationToken.None
        );

        Assert.True(first.IsSuccess);
        Assert.Equal(first.Value, replay.Value);
        Assert.Equal(ReferralProgramStatus.Active, program.Status);
        Assert.Equal(1, idempotency.ExecutedBodyCount);
    }

    [Fact]
    public async Task Issue_code_replays_metadata_only_and_conflicts_on_changed_payload()
    {
        var program = GrowthTestData.CreateActiveTenantReferralProgram();
        var programs = new InMemoryReferralProgramRepository(program);
        var codes = new InMemoryReferralCodeRepository();
        var generator = new FakeReferralCodeTokenGenerator(GeneratedToken);
        var hasher = new FakeReferralCodeTokenHasher(GeneratedToken, GrowthTestData.Sha('9'));
        var idempotency = new FakeReferralIdempotencyExecutor(codes);
        var timeProvider = new FixedTimeProvider(new DateTimeOffset(GrowthTestData.NowUtc));
        var command = new IssueTenantReferralCodeCommand(
            GrowthTestData.ReferrerTenantId,
            program.Id,
            GrowthTestData.NowUtc.AddMonths(3),
            GrowthTestData.ActorId,
            "issue-referrer-code"
        );

        var first = await IssueTenantReferralCodeHandler.Handle(
            command,
            programs,
            codes,
            generator,
            hasher,
            idempotency,
            timeProvider,
            CancellationToken.None
        );
        var replay = await IssueTenantReferralCodeHandler.Handle(
            command,
            programs,
            codes,
            generator,
            hasher,
            idempotency,
            timeProvider,
            CancellationToken.None
        );
        var conflict = await IssueTenantReferralCodeHandler.Handle(
            command with
            {
                ExpiresAtUtc = command.ExpiresAtUtc.AddDays(1),
            },
            programs,
            codes,
            generator,
            hasher,
            idempotency,
            timeProvider,
            CancellationToken.None
        );

        Assert.True(first.IsSuccess);
        Assert.Equal(first.Value, replay.Value);
        Assert.Equal(ReferralCodeStatus.Active, first.Value.Status);
        Assert.Equal("TVR-0011", first.Value.DisplayPrefix);
        Assert.Equal("EEFF", first.Value.LastFour);
        Assert.Single(codes.Items);
        Assert.Equal(GrowthTestData.Sha('9'), codes.Items.Single().CodeHash);
        Assert.Equal(1, idempotency.ExecutedBodyCount);
        Assert.True(conflict.IsFailure);
        Assert.Equal("Referrals.IdempotencyConflict", conflict.Error.Code);

        var storedShape = JsonSerializer.Serialize(first.Value);
        Assert.DoesNotContain(GeneratedToken, storedShape, StringComparison.Ordinal);
        Assert.DoesNotContain(GeneratedToken, first.Value.ToString(), StringComparison.Ordinal);
    }
}
