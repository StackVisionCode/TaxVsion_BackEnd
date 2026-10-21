using TaxVision.DataCollection.Engine.Components;

namespace TaxVision.DataCollection.Engine.Tests;

public sealed class ComponentRegistryTests
{
    public static IEnumerable<object[]> Contracts() =>
    [
        ["text", new[] { "isAnswered", "isNotAnswered", "equals", "notEquals" }],
        ["number", new[] { "isAnswered", "isNotAnswered", "equals", "notEquals", "gt", "gte", "lt", "lte" }],
        ["select", new[] { "isAnswered", "isNotAnswered", "equals", "notEquals", "in", "notIn" }],
        ["yesNo", new[] { "isTrue", "isFalse", "isAnswered", "isNotAnswered" }],
        ["checkbox", new[] { "isTrue", "isFalse" }],
        ["checkboxGroup", new[] { "isAnswered", "isNotAnswered", "contains", "notContains" }],
        ["file", new[] { "isAnswered", "isNotAnswered" }],
    ];

    [Theory]
    [MemberData(nameof(Contracts))]
    public void Operator_contract_is_closed(string type, string[] expected)
    {
        var registry = new ComponentRegistry();

        Assert.True(registry.TryGet(type, out var component));
        Assert.Equal(expected.Order(StringComparer.Ordinal), component.AllowedOperators.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Registry_contains_exactly_twenty_two_component_types()
    {
        Assert.Equal(22, new ComponentRegistry().All.Count);
    }
}
