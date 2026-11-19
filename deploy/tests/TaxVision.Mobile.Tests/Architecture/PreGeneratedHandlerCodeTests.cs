extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Wolverine;

namespace TaxVision.Mobile.Tests.Architecture;

[CollectionDefinition(nameof(PreGeneratedHandlerCodeTests), DisableParallelization = true)]
public sealed class PreGeneratedHandlerCodeCollection;

[Collection(nameof(PreGeneratedHandlerCodeTests))]
public sealed class PreGeneratedHandlerCodeTests
{
    private const string ApiProject = "src/Services/Mobile/TaxVision.Mobile.Api";

    [Fact]
    public void Every_handler_loads_from_pre_generated_code_in_production()
    {
        using var factory = ProductionHost(_ => { });

        PreGeneratedCodeRules.AssertAllHandlersArePreGenerated(factory.Services, typeof(Program).Assembly);
    }

    [Fact]
    public void Pre_generated_code_matches_the_current_handlers()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "mobile-codegen-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var factory = ProductionHost(options => options.CodeGeneration.GeneratedCodeOutputPath = scratch);

            PreGeneratedCodeRules.AssertGeneratedCodeIsCurrent(
                factory.Services,
                PreGeneratedCodeRules.GeneratedDirectory(ApiProject),
                scratch
            );
        }
        finally
        {
            if (Directory.Exists(scratch))
                Directory.Delete(scratch, recursive: true);
        }
    }

    private static WebApplicationFactory<Program> ProductionHost(Action<WolverineOptions> configure) =>
        new MobileRoutingHostFactory().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureTestServices(services => services.ConfigureWolverine(configure));
        });
}
