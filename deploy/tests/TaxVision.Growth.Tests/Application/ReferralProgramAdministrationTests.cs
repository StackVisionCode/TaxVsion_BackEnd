using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Growth.Tests.Application.Fakes;
using TaxVision.Growth.Tests.Domain;
using TaxVision.Referrals.Application.Programs.CreateTenantReferralProgram;
using TaxVision.Referrals.Application.Programs.GetActiveReferralProgram;
using TaxVision.Referrals.Application.Programs.Seeding;
using TaxVision.Referrals.Application.Programs.SuspendTenantReferralProgram;
using TaxVision.Referrals.Application.Programs.UpdateTenantReferralProgramPolicy;
using TaxVision.Referrals.Domain.Programs;
using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Growth.Tests.Application;

/// <summary>REF-0: programa por defecto sembrado, política versionada y términos vigentes.</summary>
public sealed class ReferralProgramAdministrationTests
{
    private static readonly FixedTimeProvider Clock = new(new DateTimeOffset(GrowthTestData.NowUtc));

    [Fact]
    public async Task Seeder_creates_an_active_platform_program_with_the_wallet_percentage_policy()
    {
        var programs = new InMemoryReferralProgramRepository();
        var tenant = new RecordingTenantContext();
        var unitOfWork = new CountingUnitOfWork();

        await DefaultReferralProgramSeeder.SeedAsync(
            programs,
            unitOfWork,
            tenant,
            Clock,
            NullLogger.Instance,
            CancellationToken.None
        );

        var program = Assert.Single(programs.Items);
        Assert.Equal(DefaultReferralProgramSeeder.ProgramCode, program.ProgramCode);
        Assert.Equal(ReferralProgramStatus.Active, program.Status);
        Assert.Equal(ReferralProgramScope.Platform, program.ScopeType);
        Assert.Equal(ReferralFlowType.TenantToTenant, program.FlowType);
        Assert.Equal(PlatformTenant.Id, program.TenantId);
        Assert.Equal(ReferralRewardType.WalletCredit, program.Policy.RewardType);
        Assert.Equal(1_000, program.Policy.RewardBasisPoints);
        Assert.Equal(PlatformTenant.Id, tenant.TenantId);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Seeder_is_idempotent_and_never_overwrites_what_the_platform_admin_changed()
    {
        var programs = new InMemoryReferralProgramRepository();
        var tenant = new RecordingTenantContext();
        var unitOfWork = new CountingUnitOfWork();
        await DefaultReferralProgramSeeder.SeedAsync(programs, unitOfWork, tenant, Clock, NullLogger.Instance);
        var program = programs.Items.Single();
        var tuned = ReferralProgramPolicy
            .CreateTenantToTenant(
                90,
                1,
                null,
                14,
                10,
                ReferralRewardType.WalletCredit,
                "referrals.tuned",
                rewardBasisPoints: 2_500,
                rewardCapCents: null,
                rewardCurrency: "USD"
            )
            .Value;
        Assert.True(program.Suspend(GrowthTestData.ActorId, GrowthTestData.NowUtc).IsSuccess);
        Assert.True(program.ReplacePolicy(tuned, GrowthTestData.ActorId, GrowthTestData.NowUtc).IsSuccess);

        await DefaultReferralProgramSeeder.SeedAsync(programs, unitOfWork, tenant, Clock, NullLogger.Instance);

        Assert.Single(programs.Items);
        Assert.Equal(2_500, program.Policy.RewardBasisPoints);
        Assert.Equal(ReferralProgramStatus.Suspended, program.Status);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Update_policy_replaces_the_percentage_and_leaves_an_active_program_active()
    {
        var program = ActiveProgram();
        var programs = new InMemoryReferralProgramRepository(program);

        var result = await UpdateTenantReferralProgramPolicyHandler.Handle(
            new UpdateTenantReferralProgramPolicyCommand(
                program.Id,
                new TenantReferralProgramPolicyInput(RewardBasisPoints: 1_500, RewardCapCents: 7_500),
                GrowthTestData.ActorId,
                "update-policy-1"
            ),
            programs,
            new FakeReferralIdempotencyExecutor(programs),
            Clock,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(ReferralProgramStatus.Active, program.Status);
        Assert.Equal(2, program.PolicyVersion);
        Assert.Equal(1_500, program.Policy.RewardBasisPoints);
        Assert.Equal(7_500, program.Policy.RewardCapCents);
        // lo omitido conserva el valor vigente
        Assert.Equal(30, program.Policy.WaitingPeriodDays);
        Assert.Equal("USD", program.Policy.RewardCurrency);
        Assert.Equal(1_500, result.Value.Policy.RewardBasisPoints);
    }

    [Fact]
    public async Task Update_policy_rejects_an_invalid_percentage_without_changing_the_program()
    {
        var program = ActiveProgram();
        var programs = new InMemoryReferralProgramRepository(program);

        var result = await UpdateTenantReferralProgramPolicyHandler.Handle(
            new UpdateTenantReferralProgramPolicyCommand(
                program.Id,
                new TenantReferralProgramPolicyInput(RewardBasisPoints: 20_000),
                GrowthTestData.ActorId,
                "update-policy-bad"
            ),
            programs,
            new FakeReferralIdempotencyExecutor(programs),
            Clock,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("ReferralPolicy.InvalidRewardPercentage", result.Error.Code);
        Assert.Equal(ReferralProgramStatus.Active, program.Status);
        Assert.Equal(1, program.PolicyVersion);
        Assert.Equal(1_000, program.Policy.RewardBasisPoints);
    }

    [Fact]
    public async Task Update_policy_for_a_draft_program_does_not_activate_it()
    {
        var program = DraftProgram();
        var programs = new InMemoryReferralProgramRepository(program);

        var result = await UpdateTenantReferralProgramPolicyHandler.Handle(
            new UpdateTenantReferralProgramPolicyCommand(
                program.Id,
                new TenantReferralProgramPolicyInput(WaitingPeriodDays: 7),
                GrowthTestData.ActorId,
                "update-policy-draft"
            ),
            programs,
            new FakeReferralIdempotencyExecutor(programs),
            Clock,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(ReferralProgramStatus.Draft, program.Status);
        Assert.Equal(7, program.Policy.WaitingPeriodDays);
    }

    [Fact]
    public async Task Update_policy_reports_not_found_for_an_unknown_program()
    {
        var programs = new InMemoryReferralProgramRepository();

        var result = await UpdateTenantReferralProgramPolicyHandler.Handle(
            new UpdateTenantReferralProgramPolicyCommand(
                Guid.NewGuid(),
                new TenantReferralProgramPolicyInput(),
                GrowthTestData.ActorId,
                "update-policy-missing"
            ),
            programs,
            new FakeReferralIdempotencyExecutor(programs),
            Clock,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("ReferralProgram.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Suspend_stops_the_program_from_accepting_new_referrals()
    {
        var program = ActiveProgram();
        var programs = new InMemoryReferralProgramRepository(program);

        var result = await SuspendTenantReferralProgramHandler.Handle(
            new SuspendTenantReferralProgramCommand(program.Id, GrowthTestData.ActorId, "suspend-1"),
            programs,
            new FakeReferralIdempotencyExecutor(programs),
            Clock,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(ReferralProgramStatus.Suspended, program.Status);
        Assert.True(program.EnsureAcceptingAttributions(GrowthTestData.NowUtc).IsFailure);
    }

    [Fact]
    public async Task Active_program_terms_expose_the_percentage_cap_wait_and_yearly_limit()
    {
        var programs = new InMemoryReferralProgramRepository(ActiveProgram());

        var result = await GetActiveReferralProgramHandler.Handle(
            new GetActiveReferralProgramQuery(),
            programs,
            Clock,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(ReferralRewardType.WalletCredit, result.Value.RewardType);
        Assert.Equal(1_000, result.Value.RewardBasisPoints);
        Assert.Equal(5_000, result.Value.RewardCapCents);
        Assert.Equal("USD", result.Value.RewardCurrency);
        Assert.Equal(30, result.Value.WaitingPeriodDays);
        Assert.Equal(10, result.Value.MaximumRewardsPerReferrerPerCalendarYear);
    }

    [Fact]
    public async Task Terms_are_not_found_when_the_program_is_suspended_or_missing()
    {
        var suspended = ActiveProgram();
        Assert.True(suspended.Suspend(GrowthTestData.ActorId, GrowthTestData.NowUtc).IsSuccess);

        var withSuspended = await GetActiveReferralProgramHandler.Handle(
            new GetActiveReferralProgramQuery(),
            new InMemoryReferralProgramRepository(suspended),
            Clock,
            CancellationToken.None
        );
        var empty = await GetActiveReferralProgramHandler.Handle(
            new GetActiveReferralProgramQuery(),
            new InMemoryReferralProgramRepository(),
            Clock,
            CancellationToken.None
        );

        Assert.Equal("ReferralProgram.NotFound", withSuspended.Error.Code);
        Assert.Equal("ReferralProgram.NotFound", empty.Error.Code);
    }

    private static ReferralProgram ActiveProgram()
    {
        var program = DraftProgram();
        Assert.True(program.Activate(GrowthTestData.ActorId, GrowthTestData.NowUtc).IsSuccess);
        return program;
    }

    private static ReferralProgram DraftProgram() =>
        ReferralProgram
            .Create(
                "ADMIN-TESTS",
                "Admin tests",
                ReferralProgramScope.Platform,
                tenantScopeId: null,
                ReferralFlowType.TenantToTenant,
                ReferralProgramPolicy.TenantToTenantDefaults(),
                GrowthTestData.NowUtc.AddDays(-1),
                GrowthTestData.NowUtc.AddYears(1),
                "seed-admin-tests",
                GrowthTestData.Sha('d'),
                GrowthTestData.ActorId,
                GrowthTestData.NowUtc
            )
            .Value;

    private sealed class RecordingTenantContext : ITenantContext
    {
        public Guid TenantId { get; private set; }
        public bool HasTenant => TenantId != Guid.Empty;

        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveCount++;
            return Task.FromResult(0);
        }
    }
}
