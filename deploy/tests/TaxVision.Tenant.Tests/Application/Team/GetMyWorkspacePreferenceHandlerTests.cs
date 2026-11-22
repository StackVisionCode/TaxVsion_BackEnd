using System.Text.Json;
using TaxVision.Tenant.Application.Team;
using TaxVision.Tenant.Domain.Team;

namespace TaxVision.Tenant.Tests.Application.Team;

public sealed class GetMyWorkspacePreferenceHandlerTests
{
    private const string Key = WorkspacePreferenceKeys.CrmDashboardLayout;
    private const string Mine = """[{ "id": "tasks", "size": "m", "hidden": false }]""";
    private const string Office = """[{ "id": "revenue", "size": "l", "hidden": false }]""";

    private readonly WorkspacePreferenceHarness _harness = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    [Fact]
    public async Task An_own_layout_wins_over_the_office_default()
    {
        var own = _harness.Seed(_tenantId, _userId, Mine);
        _harness.Seed(_tenantId, null, Office);

        var view = (await _harness.GetAsync(_tenantId, _userId, Key)).Value;

        Assert.Equal(Key, view.Key);
        Assert.Equal(WorkspacePreferenceSources.User, view.Source);
        Assert.Equal(1, view.SchemaVersion);
        Assert.Equal(own.Version, view.Version);
        Assert.Equal(own.UpdatedAtUtc, view.UpdatedAtUtc);
        Assert.Equal("tasks", FirstId(view.Payload));
        Assert.Equal(1, view.OfficeDefault!.Version);
        Assert.Equal("revenue", FirstId(view.OfficeDefault.Payload));
    }

    [Fact]
    public async Task Without_an_own_layout_the_office_default_applies_with_version_zero()
    {
        _harness.Seed(_tenantId, null, Office);

        var view = (await _harness.GetAsync(_tenantId, _userId, Key)).Value;

        Assert.Equal(WorkspacePreferenceSources.Office, view.Source);
        Assert.Equal(0, view.Version);
        Assert.Null(view.UpdatedAtUtc);
        Assert.Equal("revenue", FirstId(view.Payload));
    }

    [Fact]
    public async Task A_reset_own_row_falls_back_to_the_office_default_and_keeps_its_version()
    {
        var own = _harness.Seed(_tenantId, _userId, Mine);
        own.Clear(1, WorkspacePreferenceHarness.Now);
        _harness.Seed(_tenantId, null, Office);

        var view = (await _harness.GetAsync(_tenantId, _userId, Key)).Value;

        Assert.Equal(WorkspacePreferenceSources.Office, view.Source);
        Assert.Equal(2, view.Version);
        Assert.Equal("revenue", FirstId(view.Payload));
    }

    [Fact]
    public async Task With_nothing_saved_it_is_the_factory_default()
    {
        _harness.Seed(Guid.NewGuid(), null, Office);
        _harness.Seed(_tenantId, Guid.NewGuid(), Mine);

        var view = (await _harness.GetAsync(_tenantId, _userId, Key)).Value;

        Assert.Equal(WorkspacePreferenceSources.Default, view.Source);
        Assert.Equal(1, view.SchemaVersion);
        Assert.Equal(0, view.Version);
        Assert.Null(view.Payload);
        Assert.Null(view.OfficeDefault);
    }

    [Fact]
    public async Task A_removed_office_default_is_the_factory_default_but_travels_with_its_version()
    {
        var office = _harness.Seed(_tenantId, null, Office);
        office.Clear(1, WorkspacePreferenceHarness.Now);
        office.Replace(
            TaxVision.Tenant.Domain.ValueObjects.DashboardLayout.Parse(1, Office).Value,
            2,
            WorkspacePreferenceHarness.Now
        );
        office.Clear(3, WorkspacePreferenceHarness.Now);

        var view = (await _harness.GetAsync(_tenantId, _userId, Key)).Value;

        Assert.Equal(WorkspacePreferenceSources.Default, view.Source);
        Assert.Null(view.Payload);
        Assert.Equal(4, view.OfficeDefault!.Version);
        Assert.Equal(WorkspacePreferenceHarness.Now, view.OfficeDefault.UpdatedAtUtc);
        Assert.Null(view.OfficeDefault.Payload);
    }

    [Fact]
    public async Task An_unknown_key_is_rejected()
    {
        var result = await _harness.GetAsync(_tenantId, _userId, "crm-other");

        Assert.Equal(WorkspacePreferenceErrors.UnknownKey, result.Error);
    }

    private static string? FirstId(JsonElement? payload) => payload!.Value[0].GetProperty("id").GetString();
}
