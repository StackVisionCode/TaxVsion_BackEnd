using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Sessions.Policy;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Sessions;

namespace TaxVision.Auth.Tests.Application;

public sealed class SessionPolicyHandlersTests
{
    private static readonly Guid Admin = Guid.NewGuid();

    [Fact]
    public async Task Without_a_saved_policy_the_defaults_are_returned()
    {
        var result = await GetSessionPolicyHandler.Handle(
            new GetSessionPolicyQuery(),
            new FakeSessionPolicyRepository(),
            CancellationToken.None
        );

        Assert.Equal(30, result.Value.IdleTimeoutMinutes);
        Assert.Equal(60, result.Value.WarningSeconds);
        Assert.Equal(12, result.Value.AbsoluteLifetimeHours);
    }

    [Fact]
    public async Task Updating_saves_audits_with_the_reason_and_drops_the_cached_policy()
    {
        var world = new World();

        var result = await world.UpdateAsync(45, 90, 8, "Política de seguridad");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(45, result.Value.IdleTimeoutMinutes);
        var saved = Assert.Single(world.Policies.Added);
        Assert.Equal(Admin, saved.UpdatedBy);
        Assert.Equal(1, world.UnitOfWork.SaveChangesCallCount);
        var log = Assert.Single(world.Audit.Logs);
        Assert.Equal(AuthAuditAction.SessionPolicyUpdated, log.Action);
        Assert.Contains("\"reason\":\"Política de seguridad\"", log.DetailsJson);
        Assert.Contains("\"idleTimeoutMinutes\":45", log.DetailsJson);
        Assert.Equal(1, world.Gate.Invalidations);
    }

    [Fact]
    public async Task Updating_an_existing_policy_changes_the_same_row()
    {
        var world = new World(PlatformSessionPolicy.CreateDefault(DateTime.UtcNow));

        await world.UpdateAsync(20, 60, 10, "r");

        Assert.Empty(world.Policies.Added);
        Assert.Equal(20, world.Policies.Current!.IdleTimeoutMinutes);
    }

    [Fact]
    public async Task Invalid_values_save_nothing()
    {
        var world = new World();

        var result = await world.UpdateAsync(1, 60, 12, "r");

        Assert.Equal("SessionPolicy.OutOfRange", result.Error.Code);
        Assert.Equal(0, world.UnitOfWork.SaveChangesCallCount);
        Assert.Empty(world.Audit.Logs);
        Assert.Equal(0, world.Gate.Invalidations);
    }

    private sealed class World(PlatformSessionPolicy? existing = null)
    {
        public FakeSessionPolicyRepository Policies { get; } = new(existing);
        public FakeAuthAuditWriter Audit { get; } = new();
        public FakeUnitOfWork UnitOfWork { get; } = new();
        public FakeSessionLifetimeGate Gate { get; } = new();

        public Task<BuildingBlocks.Results.Result<SessionPolicyView>> UpdateAsync(
            int idle,
            int warning,
            int absolute,
            string reason
        ) =>
            UpdateSessionPolicyHandler.Handle(
                new UpdateSessionPolicyCommand(idle, warning, absolute, reason, Admin),
                Policies,
                Gate,
                Audit,
                new FakeRequestContext(),
                new FakeCorrelationContext(),
                UnitOfWork,
                CancellationToken.None
            );
    }
}

public sealed class FakeSessionPolicyRepository(PlatformSessionPolicy? existing = null) : ISessionPolicyRepository
{
    public PlatformSessionPolicy? Current { get; private set; } = existing;
    public List<PlatformSessionPolicy> Added { get; } = [];

    public Task<PlatformSessionPolicy?> GetAsync(CancellationToken ct = default) => Task.FromResult(Current);

    public Task AddAsync(PlatformSessionPolicy policy, CancellationToken ct = default)
    {
        Added.Add(policy);
        Current = policy;
        return Task.CompletedTask;
    }
}

public sealed class FakeSessionLifetimeGate(string? endReason = null) : ISessionLifetimeGate
{
    public int Invalidations { get; private set; }

    public Task<string?> EndReasonAsync(UserSession session, CancellationToken ct = default) =>
        Task.FromResult(endReason);

    public void Invalidate() => Invalidations++;
}
