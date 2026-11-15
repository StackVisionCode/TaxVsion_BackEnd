extern alias BbTesting;

using System.Security.Cryptography;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using JasperFx.CodeGeneration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;
using Wolverine.Runtime.Handlers;
using Xunit;

namespace TaxVision.Postmaster.Tests.Architecture;

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
    private const string ApiProject = "src/Services/Postmaster/TaxVision.Postmaster.Api";

    [Fact]
    public void Every_handler_loads_from_pre_generated_code_in_production()
    {
        using var factory = ProductionHost(_ => { });

        PreGeneratedCodeRules.AssertAllHandlersArePreGenerated(factory.Services, typeof(Program).Assembly);
    }

    [Fact]
    public void Pre_generated_code_matches_the_current_handlers()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "postmaster-codegen-" + Guid.NewGuid().ToString("N"));
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

    /// <summary>
    /// El código se genera una vez y corre en entornos con y sin la API key de SMTP2GO: si el contenedor cambiara
    /// de forma con la configuración, el código generado sin key no serviría con key (y viceversa).
    /// </summary>
    [Fact]
    public void Pre_generated_code_does_not_depend_on_the_smtp2go_api_key()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "postmaster-codegen-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var factory = ProductionHost(
                options => options.CodeGeneration.GeneratedCodeOutputPath = scratch,
                Smtp2GoApiKey
            );

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

    /// <summary>
    /// Con la key los pollers existen y son scoped: ningún handler puede tomarlos por constructor desde la raíz.
    /// En Development .NET valida los scopes y el handler ni se crea; en Production quedaría un poller (y su
    /// HttpClient) atrapado de por vida en el handler.
    /// </summary>
    [Fact]
    public void Every_handler_can_be_built_with_scope_validation_and_the_smtp2go_api_key()
    {
        using var factory = ProductionHost(_ => { }, Smtp2GoApiKey, validateScopes: true);
        var graph = factory.Services.GetServices<ICodeFileCollection>().OfType<HandlerGraph>().Single();

        var failures = new List<string>();
        foreach (var chain in graph.Chains)
        {
            try
            {
                graph.HandlerFor(chain.MessageType);
            }
            catch (Exception ex)
            {
                failures.Add($"{chain.MessageType.Name}: {ex.GetBaseException().Message}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    private const string Smtp2GoApiKey = "test-only-smtp2go-key";

    private static WebApplicationFactory<Program> ProductionHost(
        Action<WolverineOptions> configure,
        string? smtp2GoApiKey = null,
        bool validateScopes = false
    ) =>
        new PostmasterRoutingHostFactory().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Encryption:MasterKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            if (smtp2GoApiKey is not null)
                builder.UseSetting("DeliveryFeedback:Smtp2Go:ApiKey", smtp2GoApiKey);
            if (validateScopes)
                builder.UseDefaultServiceProvider(options => options.ValidateScopes = true);
            builder.ConfigureTestServices(services => services.ConfigureWolverine(configure));
        });
}
