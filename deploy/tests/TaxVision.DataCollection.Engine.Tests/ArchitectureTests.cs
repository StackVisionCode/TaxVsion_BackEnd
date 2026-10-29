using NetArchTest.Rules;
using TaxVision.DataCollection.Engine;

namespace TaxVision.DataCollection.Engine.Tests;

public sealed class ArchitectureTests
{
    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("Wolverine")]
    [InlineData("Microsoft.AspNetCore")]
    [InlineData("TaxVision.Tasks")]
    public void Engine_has_no_forbidden_dependencies(string dependency)
    {
        var result = Types.InAssembly(typeof(FormEngine).Assembly).ShouldNot().HaveDependencyOn(dependency).GetResult();

        Assert.True(result.IsSuccessful, string.Join(Environment.NewLine, result.FailingTypeNames ?? []));
    }
}
