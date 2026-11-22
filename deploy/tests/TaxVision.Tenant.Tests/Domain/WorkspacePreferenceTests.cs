using TaxVision.Tenant.Domain.Team;
using TaxVision.Tenant.Domain.ValueObjects;

namespace TaxVision.Tenant.Tests.Domain;

public sealed class WorkspacePreferenceTests
{
    private static readonly DateTime Created = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = Created.AddMinutes(5);

    private static readonly DashboardLayout First = Layout("""[{ "id": "tasks", "size": "m", "hidden": false }]""");
    private static readonly DashboardLayout Second = Layout("""[{ "id": "tasks", "size": "l", "hidden": true }]""");

    [Fact]
    public void Create_starts_at_version_one_with_the_canonical_content()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var preference = WorkspacePreference
            .Create(tenantId, userId, WorkspacePreferenceKeys.CrmDashboardLayout, First, Created)
            .Value;

        Assert.NotEqual(Guid.Empty, preference.Id);
        Assert.Equal(tenantId, preference.TenantId);
        Assert.Equal(userId, preference.UserId);
        Assert.Equal(WorkspacePreferenceKeys.CrmDashboardLayout, preference.PreferenceKey);
        Assert.Equal(1, preference.SchemaVersion);
        Assert.Equal(First.ToCanonicalJson(), preference.PayloadJson);
        Assert.Equal(1, preference.Version);
        Assert.Equal(Created, preference.UpdatedAtUtc);
    }

    [Fact]
    public void Create_without_user_is_the_office_default()
    {
        var preference = WorkspacePreference
            .Create(Guid.NewGuid(), null, WorkspacePreferenceKeys.CrmDashboardLayout, First, Created)
            .Value;

        Assert.Null(preference.UserId);
    }

    [Fact]
    public void Create_rejects_an_unknown_key_a_missing_office_or_an_empty_user()
    {
        Assert.Equal(
            WorkspacePreferenceErrors.UnknownKey,
            WorkspacePreference.Create(Guid.NewGuid(), null, "crm-other", First, Created).Error
        );
        Assert.Equal(
            WorkspacePreferenceErrors.TenantRequired,
            WorkspacePreference
                .Create(Guid.Empty, null, WorkspacePreferenceKeys.CrmDashboardLayout, First, Created)
                .Error
        );
        Assert.Equal(
            WorkspacePreferenceErrors.OwnerInvalid,
            WorkspacePreference
                .Create(Guid.NewGuid(), Guid.Empty, WorkspacePreferenceKeys.CrmDashboardLayout, First, Created)
                .Error
        );
    }

    [Fact]
    public void Replace_with_the_current_version_bumps_it()
    {
        var preference = New();

        var changed = preference.Replace(Second, expectedVersion: 1, Later);

        Assert.True(changed.Value);
        Assert.Equal(2, preference.Version);
        Assert.Equal(Second.ToCanonicalJson(), preference.PayloadJson);
        Assert.Equal(Later, preference.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Replace_with_another_version_is_a_conflict(long expectedVersion)
    {
        var preference = New();

        var result = preference.Replace(Second, expectedVersion, Later);

        Assert.Equal(WorkspacePreferenceErrors.VersionConflict, result.Error);
        Assert.Equal(1, preference.Version);
        Assert.Equal(First.ToCanonicalJson(), preference.PayloadJson);
    }

    [Fact]
    public void Replace_with_the_same_content_changes_nothing()
    {
        var preference = New();

        var changed = preference.Replace(First, expectedVersion: 1, Later);

        Assert.False(changed.Value);
        Assert.Equal(1, preference.Version);
        Assert.Equal(Created, preference.UpdatedAtUtc);
    }

    [Fact]
    public void Clear_empties_the_content_and_bumps_the_version()
    {
        var preference = New();

        var changed = preference.Clear(expectedVersion: 1, Later);

        Assert.True(changed.Value);
        Assert.Null(preference.PayloadJson);
        Assert.Equal(2, preference.Version);
        Assert.Equal(Later, preference.UpdatedAtUtc);
    }

    [Fact]
    public void Clear_on_an_empty_row_changes_nothing()
    {
        var preference = New();
        preference.Clear(1, Later);

        var changed = preference.Clear(expectedVersion: 2, Later.AddMinutes(1));

        Assert.False(changed.Value);
        Assert.Equal(2, preference.Version);
        Assert.Equal(Later, preference.UpdatedAtUtc);
    }

    [Fact]
    public void Clear_with_another_version_is_a_conflict()
    {
        var preference = New();

        Assert.Equal(WorkspacePreferenceErrors.VersionConflict, preference.Clear(expectedVersion: 0, Later).Error);
        Assert.Equal(First.ToCanonicalJson(), preference.PayloadJson);
    }

    [Fact]
    public void Replace_on_an_empty_row_fills_it_and_keeps_counting()
    {
        var preference = New();
        preference.Clear(1, Later);

        var changed = preference.Replace(First, expectedVersion: 2, Later.AddMinutes(1));

        Assert.True(changed.Value);
        Assert.Equal(3, preference.Version);
        Assert.Equal(First.ToCanonicalJson(), preference.PayloadJson);
    }

    private static WorkspacePreference New() =>
        WorkspacePreference
            .Create(Guid.NewGuid(), Guid.NewGuid(), WorkspacePreferenceKeys.CrmDashboardLayout, First, Created)
            .Value;

    private static DashboardLayout Layout(string json) => DashboardLayout.Parse(1, json).Value;
}
