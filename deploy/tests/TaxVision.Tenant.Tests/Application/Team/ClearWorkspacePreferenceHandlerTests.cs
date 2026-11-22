using TaxVision.Tenant.Application.Team;
using TaxVision.Tenant.Application.Team.Commands;
using TaxVision.Tenant.Domain.Team;

namespace TaxVision.Tenant.Tests.Application.Team;

public sealed class ClearWorkspacePreferenceHandlerTests
{
    private const string Key = WorkspacePreferenceKeys.CrmDashboardLayout;
    private const string Mine = """[{ "id": "tasks", "size": "m", "hidden": false }]""";
    private const string Office = """[{ "id": "revenue", "size": "l", "hidden": false }]""";

    private readonly WorkspacePreferenceHarness _harness = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    [Fact]
    public async Task Reset_empties_the_own_row_bumps_it_and_falls_back_to_the_office_default()
    {
        var own = _harness.Seed(_tenantId, _userId, Mine);
        _harness.Seed(_tenantId, null, Office);

        var view = (await ClearAsync(_userId, 1)).Value;

        Assert.Null(own.PayloadJson);
        Assert.Equal(2, own.Version);
        Assert.Equal(1, _harness.Saves);
        Assert.Equal(WorkspacePreferenceSources.Office, view.Source);
        Assert.Equal(2, view.Version);
    }

    [Fact]
    public async Task Without_a_row_and_version_zero_nothing_happens()
    {
        var view = (await ClearAsync(_userId, 0)).Value;

        Assert.Empty(_harness.Rows);
        Assert.Equal(0, _harness.Saves);
        Assert.Equal(WorkspacePreferenceSources.Default, view.Source);
        Assert.Equal(0, view.Version);
    }

    [Fact]
    public async Task Without_a_row_any_other_version_is_a_conflict()
    {
        Assert.Equal(WorkspacePreferenceErrors.VersionConflict, (await ClearAsync(_userId, 2)).Error);
    }

    [Fact]
    public async Task A_stale_version_is_a_conflict()
    {
        var own = _harness.Seed(_tenantId, _userId, Mine);
        own.Clear(1, WorkspacePreferenceHarness.Now);

        var result = await ClearAsync(_userId, 1);

        Assert.Equal(WorkspacePreferenceErrors.VersionConflict, result.Error);
        Assert.Equal(0, _harness.Saves);
    }

    [Fact]
    public async Task Resetting_an_already_empty_row_writes_nothing()
    {
        var own = _harness.Seed(_tenantId, _userId, Mine);
        own.Clear(1, WorkspacePreferenceHarness.Now);

        var view = (await ClearAsync(_userId, 2)).Value;

        Assert.Equal(2, own.Version);
        Assert.Equal(0, _harness.Saves);
        Assert.Equal(2, view.Version);
    }

    [Fact]
    public async Task Resetting_the_own_layout_does_not_touch_the_office_default()
    {
        _harness.Seed(_tenantId, _userId, Mine);
        var office = _harness.Seed(_tenantId, null, Office);

        await ClearAsync(_userId, 1);

        Assert.Equal(1, office.Version);
        Assert.NotNull(office.PayloadJson);
    }

    [Fact]
    public async Task Removing_the_office_default_keeps_the_row_with_a_new_version()
    {
        var own = _harness.Seed(_tenantId, _userId, Mine);
        var office = _harness.Seed(_tenantId, null, Office);

        var view = (await ClearAsync(owner: null, expectedVersion: 1)).Value;

        Assert.Null(office.PayloadJson);
        Assert.Equal(2, office.Version);
        Assert.Equal(1, own.Version);
        Assert.Equal(2, view.OfficeDefault!.Version);
        Assert.Null(view.OfficeDefault.Payload);
    }

    [Fact]
    public async Task An_unknown_key_is_rejected()
    {
        var result = await _harness.ClearAsync(
            new ClearWorkspacePreferenceCommand(_tenantId, _userId, _userId, "crm-other", 0)
        );

        Assert.Equal(WorkspacePreferenceErrors.UnknownKey, result.Error);
    }

    private Task<BuildingBlocks.Results.Result<WorkspacePreferenceView>> ClearAsync(
        Guid? owner,
        long expectedVersion
    ) => _harness.ClearAsync(new ClearWorkspacePreferenceCommand(_tenantId, owner, _userId, Key, expectedVersion));
}
