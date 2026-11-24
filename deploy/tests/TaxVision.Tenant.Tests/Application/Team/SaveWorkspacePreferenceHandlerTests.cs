using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Tenant.Application.Team;
using TaxVision.Tenant.Application.Team.Abstractions;
using TaxVision.Tenant.Application.Team.Commands;
using TaxVision.Tenant.Application.Team.Consumers;
using TaxVision.Tenant.Application.Team.Queries;
using TaxVision.Tenant.Domain.Team;
using TaxVision.Tenant.Domain.ValueObjects;

namespace TaxVision.Tenant.Tests.Application.Team;

public sealed class SaveWorkspacePreferenceHandlerTests
{
    private const string Key = WorkspacePreferenceKeys.CrmDashboardLayout;
    private const string Mine = """[{ "id": "tasks", "size": "m", "hidden": false }]""";
    private const string Changed = """[{ "id": "tasks", "size": "l", "hidden": true }]""";

    private readonly WorkspacePreferenceHarness _harness = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    [Fact]
    public async Task Without_a_row_version_zero_creates_it_at_version_one()
    {
        var view = (await SaveAsync(_userId, 0, Mine)).Value;

        var row = Assert.Single(_harness.Rows);
        Assert.Equal(_userId, row.UserId);
        Assert.Equal(1, row.Version);
        Assert.Equal(1, _harness.Saves);
        Assert.Equal(WorkspacePreferenceSources.User, view.Source);
        Assert.Equal(1, view.Version);
        Assert.Equal("tasks", view.Payload!.Value[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Without_a_row_any_other_version_is_a_conflict()
    {
        var result = await SaveAsync(_userId, 3, Mine);

        Assert.Equal(WorkspacePreferenceErrors.VersionConflict, result.Error);
        Assert.Empty(_harness.Rows);
        Assert.Equal(0, _harness.Saves);
    }

    [Fact]
    public async Task With_the_current_version_it_replaces_and_bumps()
    {
        var row = _harness.Seed(_tenantId, _userId, Mine);

        var view = (await SaveAsync(_userId, 1, Changed)).Value;

        Assert.Equal(2, row.Version);
        Assert.Equal(2, view.Version);
        Assert.Equal(DashboardLayout.Parse(1, Changed).Value.ToCanonicalJson(), row.PayloadJson);
        Assert.Equal(1, _harness.Saves);
    }

    [Fact]
    public async Task A_stale_version_is_a_conflict_and_saves_nothing()
    {
        var row = _harness.Seed(_tenantId, _userId, Mine);
        row.Replace(DashboardLayout.Parse(1, Changed).Value, 1, WorkspacePreferenceHarness.Now);

        var result = await SaveAsync(_userId, 1, Mine);

        Assert.Equal(WorkspacePreferenceErrors.VersionConflict, result.Error);
        Assert.Equal(2, row.Version);
        Assert.Equal(0, _harness.Saves);
    }

    [Fact]
    public async Task The_same_content_neither_saves_nor_bumps()
    {
        var row = _harness.Seed(_tenantId, _userId, Mine);

        var view = (await SaveAsync(_userId, 1, Mine)).Value;

        Assert.Equal(1, row.Version);
        Assert.Equal(1, view.Version);
        Assert.Equal(0, _harness.Saves);
    }

    [Fact]
    public async Task An_unknown_key_is_rejected_before_the_schema_and_the_payload()
    {
        var result = await SaveWorkspacePreferenceHandler.Handle(
            new SaveWorkspacePreferenceCommand(_tenantId, _userId, _userId, "crm-other", 2, 0, "not json"),
            _harness,
            _harness,
            _harness.Clock,
            NullLogger<WorkspacePreference>.Instance,
            CancellationToken.None
        );

        Assert.Equal(WorkspacePreferenceErrors.UnknownKey, result.Error);
        Assert.Equal(0, _harness.Saves);
    }

    [Fact]
    public async Task An_invalid_payload_saves_nothing()
    {
        var result = await SaveAsync(_userId, 0, """[{ "id": "tasks", "size": "xl", "hidden": false }]""");

        Assert.Equal(WorkspacePreferenceErrors.InvalidWidgetSize, result.Error);
        Assert.Empty(_harness.Rows);
    }

    [Fact]
    public async Task The_office_default_can_be_set_again_after_it_was_removed()
    {
        var office = _harness.Seed(_tenantId, null, Mine);
        office.Clear(1, WorkspacePreferenceHarness.Now);

        var result = await SaveAsync(owner: null, expectedVersion: 2, Changed);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, office.Version);
        Assert.NotNull(office.PayloadJson);
        Assert.Equal(3, result.Value.OfficeDefault!.Version);
        Assert.NotNull(result.Value.OfficeDefault.Payload);
    }

    [Fact]
    public async Task Setting_the_office_default_does_not_touch_the_actor_layout()
    {
        var own = _harness.Seed(_tenantId, _userId, Mine);

        var view = (await SaveAsync(owner: null, expectedVersion: 0, Changed)).Value;

        Assert.Equal(1, own.Version);
        Assert.Equal(2, _harness.Rows.Count);
        Assert.Equal(WorkspacePreferenceSources.User, view.Source);
        Assert.Equal(1, view.OfficeDefault!.Version);
    }

    [Fact]
    public async Task The_task_board_preferences_are_saved_under_their_own_key()
    {
        var result = await SaveWorkspacePreferenceHandler.Handle(
            new SaveWorkspacePreferenceCommand(
                _tenantId,
                _userId,
                _userId,
                WorkspacePreferenceKeys.CrmTasksBoard,
                1,
                0,
                """{ "groupBy": "stage", "hiddenStatusCodes": ["review"], "collapsedStatusCodes": [], "hideEmpty": true, "sort": "Due" }"""
            ),
            _harness,
            _harness,
            _harness.Clock,
            NullLogger<WorkspacePreference>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("stage", result.Value.Payload!.Value.GetProperty("groupBy").GetString());
        Assert.Equal(WorkspacePreferenceKeys.CrmTasksBoard, Assert.Single(_harness.Rows).PreferenceKey);
    }

    private Task<Result<WorkspacePreferenceView>> SaveAsync(Guid? owner, long expectedVersion, string json) =>
        SaveWorkspacePreferenceHandler.Handle(
            new SaveWorkspacePreferenceCommand(_tenantId, owner, _userId, Key, 1, expectedVersion, json),
            _harness,
            _harness,
            _harness.Clock,
            NullLogger<WorkspacePreference>.Instance,
            CancellationToken.None
        );
}

/// <summary>Repositorio, unidad y correlación en memoria para los handlers de preferencias.</summary>
internal sealed class WorkspacePreferenceHarness : IWorkspacePreferenceRepository, IUnitOfWork, ICorrelationContext
{
    public static readonly DateTime Now = new(2026, 11, 20, 15, 0, 0, DateTimeKind.Utc);

    private readonly List<WorkspacePreference> _pending = [];

    public List<WorkspacePreference> Rows { get; } = [];
    public List<string> Log { get; } = [];
    public int Saves { get; private set; }
    public TimeProvider Clock { get; } = new FixedClock(new DateTimeOffset(Now));

    public string CorrelationId => "test-correlation-id";

    public WorkspacePreference Seed(Guid tenantId, Guid? userId, string json)
    {
        var layout = DashboardLayout.Parse(1, json).Value;
        var row = WorkspacePreference
            .Create(tenantId, userId, WorkspacePreferenceKeys.CrmDashboardLayout, layout, Now.AddDays(-1))
            .Value;
        Rows.Add(row);
        return row;
    }

    public Task<WorkspacePreference?> GetForUserAsync(Guid tenantId, Guid userId, string key, CancellationToken ct) =>
        Task.FromResult(
            Rows.SingleOrDefault(p => p.TenantId == tenantId && p.UserId == userId && p.PreferenceKey == key)
        );

    public Task<WorkspacePreference?> GetOfficeDefaultAsync(Guid tenantId, string key, CancellationToken ct) =>
        Task.FromResult(
            Rows.SingleOrDefault(p => p.TenantId == tenantId && p.UserId == null && p.PreferenceKey == key)
        );

    public void Add(WorkspacePreference preference) => _pending.Add(preference);

    public Task<int> DeleteAllOfUserAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        Log.Add("delete");
        return Task.FromResult(Rows.RemoveAll(p => p.TenantId == tenantId && p.UserId == userId));
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        Saves++;
        Rows.AddRange(_pending);
        var count = _pending.Count;
        _pending.Clear();
        return Task.FromResult(count);
    }

    public void Set(string correlationId) { }

    public IDisposable Push(string correlationId)
    {
        Log.Add($"push:{correlationId}");
        return new Scope();
    }

    public Task<Result<WorkspacePreferenceView>> GetAsync(Guid tenantId, Guid userId, string key) =>
        GetMyWorkspacePreferenceHandler.Handle(
            new GetMyWorkspacePreferenceQuery(tenantId, userId, key),
            this,
            CancellationToken.None
        );

    public Task<Result<WorkspacePreferenceView>> ClearAsync(ClearWorkspacePreferenceCommand command) =>
        ClearWorkspacePreferenceHandler.Handle(
            command,
            this,
            this,
            Clock,
            NullLogger<WorkspacePreference>.Instance,
            CancellationToken.None
        );

    public Task OffboardAsync(UserOffboardedIntegrationEvent evt) =>
        WorkspacePreferencesUserOffboardedConsumer.Handle(
            evt,
            this,
            this,
            NullLogger<WorkspacePreference>.Instance,
            CancellationToken.None
        );

    private sealed class Scope : IDisposable
    {
        public void Dispose() { }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
