using TaxVision.Tenant.Domain.Team;
using TaxVision.Tenant.Domain.ValueObjects;

namespace TaxVision.Tenant.Tests.Domain;

/// <summary>Preferencias del tablero de tareas: agrupación, columnas ocultas y plegadas, vacías y orden.</summary>
public sealed class TaskBoardPreferenceTests
{
    [Fact]
    public void The_canonical_json_keeps_only_the_known_fields_in_a_fixed_order()
    {
        var preference = TaskBoardPreference
            .Parse(
                1,
                """{ "sort": "Priority", "hideEmpty": true, "collapsedStatusCodes": ["waiting"], "hiddenStatusCodes": ["review","review"], "groupBy": "status", "extra": 1 }"""
            )
            .Value;

        Assert.Equal(
            """{"groupBy":"status","hiddenStatusCodes":["review"],"collapsedStatusCodes":["waiting"],"hideEmpty":true,"sort":"Priority"}""",
            preference.ToCanonicalJson()
        );
    }

    [Fact]
    public void A_board_that_has_not_chosen_a_grouping_yet_is_valid()
    {
        var preference = TaskBoardPreference
            .Parse(
                1,
                """{ "groupBy": null, "hiddenStatusCodes": [], "collapsedStatusCodes": [], "hideEmpty": false, "sort": "Due" }"""
            )
            .Value;

        Assert.Contains("\"groupBy\":null", preference.ToCanonicalJson());
    }

    [Theory]
    [InlineData("""{ "groupBy": "column" }""")]
    [InlineData("""{ "sort": "Title" }""")]
    [InlineData("""{ "hiddenStatusCodes": ["Not A Code"] }""")]
    [InlineData("""[1, 2]""")]
    [InlineData("""not json""")]
    public void Anything_else_is_rejected(string json)
    {
        Assert.Equal(WorkspacePreferenceErrors.InvalidBoardPreferences, TaskBoardPreference.Parse(1, json).Error);
    }

    [Fact]
    public void Too_many_codes_are_rejected()
    {
        var codes = string.Join(",", Enumerable.Range(0, TaskBoardPreference.MaxCodes + 1).Select(i => $"\"s{i}\""));

        Assert.Equal(
            WorkspacePreferenceErrors.InvalidBoardPreferences,
            TaskBoardPreference.Parse(1, $$"""{ "hiddenStatusCodes": [{{codes}}] }""").Error
        );
    }

    [Fact]
    public void An_unknown_schema_is_rejected()
    {
        Assert.Equal(WorkspacePreferenceErrors.UnsupportedSchema, TaskBoardPreference.Parse(2, "{}").Error);
    }

    [Fact]
    public void Each_key_uses_its_own_validator()
    {
        var board = WorkspacePreferenceContent.Parse(
            WorkspacePreferenceKeys.CrmTasksBoard,
            1,
            """{ "hideEmpty": true }"""
        );
        var dashboardUnderBoardKey = WorkspacePreferenceContent.Parse(
            WorkspacePreferenceKeys.CrmTasksBoard,
            1,
            """[{ "id": "tasks", "size": "m", "hidden": false }]"""
        );

        Assert.True(board.IsSuccess);
        Assert.Equal(WorkspacePreferenceErrors.InvalidBoardPreferences, dashboardUnderBoardKey.Error);
        Assert.Equal(1, WorkspacePreferenceContent.DefaultSchemaOf(WorkspacePreferenceKeys.CrmTasksBoard));
    }
}
