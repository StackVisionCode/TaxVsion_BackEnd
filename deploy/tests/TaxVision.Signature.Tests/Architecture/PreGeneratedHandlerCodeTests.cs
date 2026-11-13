extern alias BbTesting;

using System.Security.Cryptography;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Wolverine;
using Xunit;

namespace TaxVision.Signature.Tests.Architecture;

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
    private const string ApiProject = "src/Services/Signature/TaxVision.Signature.Api";

    [Fact]
    public void Every_handler_loads_from_pre_generated_code_in_production()
    {
        using var factory = ProductionHost(_ => { });

        PreGeneratedCodeRules.AssertAllHandlersArePreGenerated(factory.Services, typeof(Program).Assembly);
    }

    [Fact]
    public void Pre_generated_code_matches_the_current_handlers()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "signature-codegen-" + Guid.NewGuid().ToString("N"));
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
        new SignatureRoutingHostFactory().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Encryption:MasterKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            // Fuera de Development la clave del firmante tiene que ser un PEM montado.
            builder.UseSetting("Signature:SignerJwt:PrivateKeyPem", SignerKeyPem.Value);
            builder.ConfigureTestServices(services => services.ConfigureWolverine(configure));
        });

    private static readonly Lazy<string> SignerKeyPem = new(() =>
    {
        var path = Path.Combine(Path.GetTempPath(), "signature-codegen-signer.pem");
        using var rsa = RSA.Create(2048);
        File.WriteAllText(path, rsa.ExportRSAPrivateKeyPem());
        return path;
    });
}
