extern alias BbTesting;

using System.Security.Cryptography;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Wolverine;
using Xunit;

namespace TaxVision.Customer.Tests.Architecture;

/// <summary>
/// Aislada: generar código prende una bandera estática de JasperFx que deja sin transportes a cualquier host que
/// arranque en paralelo en el mismo proceso.
/// </summary>
[CollectionDefinition(nameof(PreGeneratedHandlerCodeTests), DisableParallelization = true)]
public sealed class PreGeneratedHandlerCodeCollection;

/// <summary>
/// Producción corre en Static: sin Roslyn ni compilación al primer uso (eran cientos de MB por handler). Cada
/// handler tiene que existir ya compilado en el Api y estar al día con el código. El host es el de producción
/// (mismo registro de servicios que <c>codegen write</c>), sin base ni Rabbit.
/// </summary>
[Collection(nameof(PreGeneratedHandlerCodeTests))]
public sealed class PreGeneratedHandlerCodeTests
{
    private const string ApiProject = "src/Services/Customer/TaxVision.Customer.Api";

    [Fact]
    public void Every_handler_loads_from_pre_generated_code_in_production()
    {
        using var factory = ProductionHost(_ => { });

        PreGeneratedCodeRules.AssertAllHandlersArePreGenerated(factory.Services, typeof(Program).Assembly);
    }

    [Fact]
    public void Pre_generated_code_matches_the_current_handlers()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "customer-codegen-" + Guid.NewGuid().ToString("N"));
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
        new CustomerRoutingHostFactory().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Encryption:MasterKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            builder.ConfigureTestServices(services => services.ConfigureWolverine(configure));
        });
}
