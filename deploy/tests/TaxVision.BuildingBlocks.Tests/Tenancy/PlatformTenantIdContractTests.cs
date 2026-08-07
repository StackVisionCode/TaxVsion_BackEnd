using System.Text.RegularExpressions;
using BuildingBlocks.Tenancy;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Tenancy;

/// <summary>
/// H-17. El GUID del tenant de plataforma está <b>duplicado</b>: la autoridad es
/// <see cref="PlatformTenant.Id"/> en .NET, pero Communication (Node) lo recibe como la variable de
/// entorno <c>COMMUNICATION_PLATFORM_TENANT_ID</c>, que es una copia literal en el compose y en el
/// <c>.env</c>. Nada obligaba a que coincidieran.
///
/// <para>
/// Si divergen, el fallo es silencioso y feo: Communication trataría los tickets de soporte
/// cross-tenant como si fueran de un tenant que no existe — sin excepción, sin log, solo listados
/// vacíos. Estos tests convierten esa coincidencia en algo que el build verifica.
/// </para>
/// </summary>
public sealed class PlatformTenantIdContractTests
{
    private const string EnvKey = "COMMUNICATION_PLATFORM_TENANT_ID";

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>Todos los valores declarados para la clave, vengan del compose o del .env.</summary>
    private static IEnumerable<(string Source, string Value)> DeclaredValues()
    {
        var root = RepositoryRoot();
        var files = new[]
        {
            Path.Combine(root, "deploy", "docker", "docker-compose.yml"),
            Path.Combine(root, "src", "Services", "Communication", ".env"),
        };

        // Cubre tanto "CLAVE: valor" (YAML) como "CLAVE=valor" (.env).
        var pattern = new Regex($@"{EnvKey}\s*[:=]\s*""?([0-9a-fA-F-]{{36}})""?");

        foreach (var file in files.Where(File.Exists))
        {
            foreach (Match m in pattern.Matches(File.ReadAllText(file)))
                yield return (Path.GetFileName(file), m.Groups[1].Value);
        }
    }

    [Fact]
    public void ElGuidDeNodeCoincideConLaConstanteDeDotNet()
    {
        var declared = DeclaredValues().ToArray();

        Assert.True(
            declared.Length > 0,
            $"No se encontro ninguna declaracion de {EnvKey}. Si la clave se renombro, actualizar este test."
        );

        var mismatches = declared
            .Where(d => !Guid.TryParse(d.Value, out var g) || g != PlatformTenant.Id)
            .Select(d => $"{d.Source} = {d.Value}")
            .ToArray();

        Assert.True(
            mismatches.Length == 0,
            $"{EnvKey} no coincide con PlatformTenant.Id ({PlatformTenant.Id}): {string.Join("; ", mismatches)}. "
                + "La autoridad es la constante de .NET; Node recibe una copia por env var."
        );
    }

    [Fact]
    public void LaConstanteNoEsUnGuidDegenerado()
    {
        // Guid.Empty como tenant de plataforma colisionaria con el sentinela "sin tenant" que usan
        // varios middlewares, y el bug seria practicamente indetectable.
        Assert.NotEqual(Guid.Empty, PlatformTenant.Id);
    }
}
