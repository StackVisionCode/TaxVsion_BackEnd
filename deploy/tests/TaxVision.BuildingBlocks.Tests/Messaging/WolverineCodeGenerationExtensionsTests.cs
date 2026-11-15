using BuildingBlocks.Web.Hosting;
using JasperFx.CodeGeneration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Messaging;

/// <summary>Solo Production usa el código pregenerado; Testing también es "no Development" y no debe pasar a Static.</summary>
public sealed class WolverineCodeGenerationExtensionsTests
{
    [Theory]
    [InlineData("Production", TypeLoadMode.Static)]
    [InlineData("Development", TypeLoadMode.Dynamic)]
    [InlineData("Testing", TypeLoadMode.Dynamic)]
    [InlineData("Staging", TypeLoadMode.Dynamic)]
    public void Only_production_loads_pre_generated_code(string environment, TypeLoadMode expected)
    {
        Assert.Equal(
            expected,
            WolverineCodeGenerationExtensions.ResolveTypeLoadMode(new Env(environment), Config(null))
        );
    }

    [Fact]
    public void The_mode_can_be_forced_by_configuration()
    {
        Assert.Equal(
            TypeLoadMode.Static,
            WolverineCodeGenerationExtensions.ResolveTypeLoadMode(new Env("Development"), Config("static"))
        );
    }

    private static IConfiguration Config(string? mode) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                mode is null
                    ? []
                    : new Dictionary<string, string?> { [WolverineCodeGenerationExtensions.TypeLoadModeKey] = mode }
            )
            .Build();

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
