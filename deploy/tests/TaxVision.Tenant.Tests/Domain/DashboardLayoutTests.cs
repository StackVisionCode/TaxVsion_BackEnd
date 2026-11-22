using TaxVision.Tenant.Domain.Team;
using TaxVision.Tenant.Domain.ValueObjects;

namespace TaxVision.Tenant.Tests.Domain;

public sealed class DashboardLayoutTests
{
    private const string Valid = """
        [
          { "id": "needs-attention", "size": "l", "hidden": false },
          { "id": "tasks", "size": "m", "hidden": false },
          { "id": "revenue", "size": "s", "hidden": true }
        ]
        """;

    [Fact]
    public void A_valid_layout_keeps_the_order_sizes_and_visibility()
    {
        var layout = DashboardLayout.Parse(1, Valid).Value;

        Assert.Equal(1, layout.SchemaVersion);
        Assert.Equal(
            [
                new DashboardWidgetPlacement("needs-attention", "l", false),
                new DashboardWidgetPlacement("tasks", "m", false),
                new DashboardWidgetPlacement("revenue", "s", true),
            ],
            layout.Items
        );
    }

    [Fact]
    public void The_canonical_json_keeps_only_id_size_and_hidden()
    {
        var layout = DashboardLayout
            .Parse(1, """[{ "hidden": true, "config": { "x": 1 }, "size": "m", "id": "tasks", "extra": "drop" }]""")
            .Value;

        Assert.Equal("""[{"id":"tasks","size":"m","hidden":true}]""", layout.ToCanonicalJson());
    }

    [Fact]
    public void An_empty_array_is_a_valid_layout()
    {
        Assert.Equal("[]", DashboardLayout.Parse(1, "[]").Value.ToCanonicalJson());
    }

    [Fact]
    public void Fifty_widgets_are_allowed_and_fifty_one_are_too_many()
    {
        Assert.True(DashboardLayout.Parse(1, Items(50)).IsSuccess);
        Assert.Equal(WorkspacePreferenceErrors.TooManyItems, DashboardLayout.Parse(1, Items(51)).Error);
    }

    [Theory]
    [InlineData("""[{ "id": "Tasks", "size": "m", "hidden": false }]""")]
    [InlineData("""[{ "id": "", "size": "m", "hidden": false }]""")]
    [InlineData("""[{ "id": "tasks_due", "size": "m", "hidden": false }]""")]
    [InlineData("""[{ "id": "a1234567890123456789012345678901234567890", "size": "m", "hidden": false }]""")]
    [InlineData("""[{ "id": 7, "size": "m", "hidden": false }]""")]
    [InlineData("""[{ "size": "m", "hidden": false }]""")]
    public void A_malformed_widget_id_is_rejected(string json)
    {
        Assert.Equal(WorkspacePreferenceErrors.InvalidWidgetId, DashboardLayout.Parse(1, json).Error);
    }

    [Fact]
    public void A_repeated_widget_id_is_rejected()
    {
        var json =
            """[{ "id": "tasks", "size": "m", "hidden": false }, { "id": "tasks", "size": "s", "hidden": true }]""";

        Assert.Equal(WorkspacePreferenceErrors.DuplicateWidgetId, DashboardLayout.Parse(1, json).Error);
    }

    [Theory]
    [InlineData("""[{ "id": "tasks", "size": "xl", "hidden": false }]""")]
    [InlineData("""[{ "id": "tasks", "size": "M", "hidden": false }]""")]
    [InlineData("""[{ "id": "tasks", "hidden": false }]""")]
    [InlineData("""[{ "id": "tasks", "size": 1, "hidden": false }]""")]
    public void A_size_outside_s_m_l_is_rejected(string json)
    {
        Assert.Equal(WorkspacePreferenceErrors.InvalidWidgetSize, DashboardLayout.Parse(1, json).Error);
    }

    [Theory]
    [InlineData("""[{ "id": "tasks", "size": "m" }]""")]
    [InlineData("""[{ "id": "tasks", "size": "m", "hidden": "false" }]""")]
    [InlineData("""[{ "id": "tasks", "size": "m", "hidden": null }]""")]
    public void Hidden_must_be_a_boolean(string json)
    {
        Assert.Equal(WorkspacePreferenceErrors.InvalidPayload, DashboardLayout.Parse(1, json).Error);
    }

    [Theory]
    [InlineData("""{ "id": "tasks" }""")]
    [InlineData("null")]
    [InlineData("\"tasks\"")]
    [InlineData("[1, 2]")]
    [InlineData("not json")]
    [InlineData("")]
    public void A_payload_that_is_not_an_array_of_objects_is_rejected(string json)
    {
        Assert.Equal(WorkspacePreferenceErrors.InvalidPayload, DashboardLayout.Parse(1, json).Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Only_schema_versions_one_and_two_are_supported(int schemaVersion)
    {
        Assert.Equal(WorkspacePreferenceErrors.UnsupportedSchema, DashboardLayout.Parse(schemaVersion, Valid).Error);
    }

    [Fact]
    public void Schema_two_keeps_the_scope_of_each_widget()
    {
        var layout = DashboardLayout
            .Parse(
                2,
                """
                [
                  { "id": "tasks", "size": "m", "hidden": false, "scope": "office" },
                  { "id": "signatures", "size": "s", "hidden": false, "scope": "me" },
                  { "id": "revenue", "size": "l", "hidden": true }
                ]
                """
            )
            .Value;

        Assert.Equal(2, layout.SchemaVersion);
        Assert.Equal(
            [
                new DashboardWidgetPlacement("tasks", "m", false, "office"),
                new DashboardWidgetPlacement("signatures", "s", false, "me"),
                new DashboardWidgetPlacement("revenue", "l", true),
            ],
            layout.Items
        );
        Assert.Equal(
            """[{"id":"tasks","size":"m","hidden":false,"scope":"office"},{"id":"signatures","size":"s","hidden":false,"scope":"me"},{"id":"revenue","size":"l","hidden":true}]""",
            layout.ToCanonicalJson()
        );
    }

    [Fact]
    public void Schema_one_drops_the_scope()
    {
        var layout = DashboardLayout
            .Parse(1, """[{ "id": "tasks", "size": "m", "hidden": false, "scope": "office" }]""")
            .Value;

        Assert.Null(Assert.Single(layout.Items).Scope);
        Assert.Equal("""[{"id":"tasks","size":"m","hidden":false}]""", layout.ToCanonicalJson());
    }

    [Theory]
    [InlineData("""[{ "id": "tasks", "size": "m", "hidden": false, "scope": "everyone" }]""")]
    [InlineData("""[{ "id": "tasks", "size": "m", "hidden": false, "scope": "Office" }]""")]
    [InlineData("""[{ "id": "tasks", "size": "m", "hidden": false, "scope": 1 }]""")]
    [InlineData("""[{ "id": "tasks", "size": "m", "hidden": false, "scope": "" }]""")]
    public void A_scope_other_than_me_or_office_is_rejected(string json)
    {
        Assert.Equal(WorkspacePreferenceErrors.InvalidWidgetScope, DashboardLayout.Parse(2, json).Error);
    }

    [Fact]
    public void The_rules_apply_in_the_agreed_order()
    {
        // Esquema antes que forma.
        Assert.Equal(WorkspacePreferenceErrors.UnsupportedSchema, DashboardLayout.Parse(3, "not json").Error);
        // Tope antes que el contenido de cada elemento.
        Assert.Equal(WorkspacePreferenceErrors.TooManyItems, DashboardLayout.Parse(1, Items(51, id: "BAD")).Error);
        // Id antes que repetidos, tamaño y hidden, aunque el error esté en otro elemento.
        Assert.Equal(
            WorkspacePreferenceErrors.InvalidWidgetId,
            DashboardLayout
                .Parse(
                    1,
                    """[{ "id": "a", "size": "x" }, { "id": "a", "size": "m", "hidden": false }, { "id": "B", "size": "m", "hidden": false }]"""
                )
                .Error
        );
        // Repetidos antes que tamaño.
        Assert.Equal(
            WorkspacePreferenceErrors.DuplicateWidgetId,
            DashboardLayout
                .Parse(1, """[{ "id": "a", "size": "x" }, { "id": "a", "size": "m", "hidden": false }]""")
                .Error
        );
        // Tamaño antes que hidden.
        Assert.Equal(
            WorkspacePreferenceErrors.InvalidWidgetSize,
            DashboardLayout
                .Parse(1, """[{ "id": "a", "size": "m" }, { "id": "b", "size": "x", "hidden": false }]""")
                .Error
        );
    }

    private static string Items(int count, string? id = null) =>
        "["
        + string.Join(
            ",",
            Enumerable.Range(0, count).Select(i => $$"""{ "id": "{{id ?? $"w-{i}"}}", "size": "m", "hidden": false }""")
        )
        + "]";
}
