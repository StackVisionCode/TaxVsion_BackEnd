using TaxVision.Tasks.Application.Tasks;

namespace TaxVision.Tasks.Tests.Tasks;

/// <summary>El parámetro <c>scope</c> de los resúmenes: vacío = como siempre; solo "me" u "office".</summary>
public sealed class DashboardScopesTests
{
    [Theory]
    [InlineData(null, DashboardScope.Default)]
    [InlineData("", DashboardScope.Default)]
    [InlineData("me", DashboardScope.Me)]
    [InlineData("Office", DashboardScope.Office)]
    public void Known_values_parse(string? raw, DashboardScope expected)
    {
        Assert.True(DashboardScopes.TryParse(raw, out var scope));
        Assert.Equal(expected, scope);
    }

    [Theory]
    [InlineData("all")]
    [InlineData("2")]
    [InlineData("Default")]
    public void Anything_else_is_rejected(string raw) => Assert.False(DashboardScopes.TryParse(raw, out _));
}
