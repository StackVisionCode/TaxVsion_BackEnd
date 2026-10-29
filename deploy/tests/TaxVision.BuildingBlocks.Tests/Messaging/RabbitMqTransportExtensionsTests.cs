using BuildingBlocks.Web.Hosting;
using Xunit;
using Xunit.Abstractions;

namespace TaxVision.BuildingBlocks.Tests.Messaging;

/// <summary>
/// Todo servicio .NET abre RabbitMQ por <see cref="RabbitMqTransportExtensions.UseTaxVisionRabbitMq(Wolverine.WolverineOptions, Uri)"/>,
/// que activa las confirmaciones de publicación.
///
/// <para>
/// Sin confirmaciones, Wolverine 6.14 da por enviado lo que el broker rechaza después y el outbox
/// borra la fila: medido en T-2 de Client Forms con un canal cerrado por 403. Un <c>UseRabbitMq</c>
/// directo en un servicio nuevo volvería a ese comportamiento sin que nada lo note.
/// </para>
/// </summary>
public sealed class RabbitMqTransportExtensionsTests(ITestOutputHelper output)
{
    private static DirectoryInfo RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    private static IEnumerable<FileInfo> SourceFiles() =>
        new DirectoryInfo(Path.Combine(RepositoryRoot().FullName, "src"))
            .EnumerateFiles("*.cs", SearchOption.AllDirectories)
            .Where(f =>
                !f.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !f.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
            );

    [Fact]
    public void NingunServicioAbreRabbitMqSinElHelperCompartido()
    {
        var offenders = SourceFiles()
            .Where(f => f.Name != "RabbitMqTransportExtensions.cs")
            .Where(f => File.ReadAllText(f.FullName).Contains(".UseRabbitMq(", StringComparison.Ordinal))
            .Select(f => f.FullName)
            .Order()
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "Estos archivos abren RabbitMQ con UseRabbitMq(...) en vez de "
                + $"{nameof(RabbitMqTransportExtensions.UseTaxVisionRabbitMq)}(...), que activa las confirmaciones "
                + "de publicación:\n  "
                + string.Join("\n  ", offenders)
        );
    }

    [Fact]
    public void LosServiciosConRabbitMqUsanElHelper()
    {
        var users = SourceFiles()
            .Where(f =>
                File.ReadAllText(f.FullName)
                    .Contains(nameof(RabbitMqTransportExtensions.UseTaxVisionRabbitMq), StringComparison.Ordinal)
            )
            .Count(f => f.Name != "RabbitMqTransportExtensions.cs");

        output.WriteLine($"Servicios que abren RabbitMQ con confirmaciones: {users}");
        Assert.True(users > 0, "Ningún servicio usa el helper: el test buscaría en el sitio equivocado.");
    }
}
