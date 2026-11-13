extern alias BbTesting;

using System.Security.Cryptography;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaxVision.Growth.Infrastructure.Persistence;
using Wolverine;
using Xunit;

namespace TaxVision.Growth.Tests.Architecture;

/// <summary>
/// Producción corre en Static: sin Roslyn ni compilación al primer uso (eran cientos de MB por handler). Cada
/// handler tiene que existir ya compilado en el Api y estar al día con el código. El host es el de producción
/// (mismo registro de servicios que <c>codegen write</c>), sin base ni Rabbit.
/// </summary>
/// <summary>
/// Aislada: generar código prende una bandera estática de JasperFx que deja sin transportes a cualquier host que
/// arranque en paralelo en el mismo proceso.
/// </summary>
[CollectionDefinition(nameof(PreGeneratedHandlerCodeTests), DisableParallelization = true)]
public sealed class PreGeneratedHandlerCodeCollection;

[Collection(nameof(PreGeneratedHandlerCodeTests))]
public sealed class PreGeneratedHandlerCodeTests
{
    private const string ApiProject = "src/Services/Growth/TaxVision.Growth.Api";

    [Fact]
    public void Every_handler_loads_from_pre_generated_code_in_production()
    {
        using var factory = ProductionHost(_ => { });

        PreGeneratedCodeRules.AssertAllHandlersArePreGenerated(factory.Services, typeof(Program).Assembly);
    }

    [Fact]
    public void Pre_generated_code_matches_the_current_handlers()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "growth-codegen-" + Guid.NewGuid().ToString("N"));
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
        new GrowthRoutingHostFactory().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Encryption:MasterKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            builder.ConfigureTestServices(services =>
            {
                // Program siembra el programa de referidos al arrancar: base en memoria, mismo registro de servicios.
                services.RemoveAll<IDbContextOptionsConfiguration<GrowthDbContext>>();
                services.RemoveAll<DbContextOptions<GrowthDbContext>>();
                services.AddDbContext<GrowthDbContext>(options => options.UseInMemoryDatabase("growth-codegen"));
                services.ConfigureWolverine(configure);
            });
        });
}
