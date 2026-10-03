using System.Text.RegularExpressions;

namespace TaxVision.Notification.Tests.Architecture;

/// <summary>
/// Ningún consumer arma el <c>EmailDispatchRequest</c> a mano: todos pasan por
/// <c>ScribeRenderedEmailExtensions.ToDispatchRequest</c>.
///
/// <para>El motivo es el campo que se cae en silencio. Copiar campo por campo hace que olvidarse de
/// uno no se note — el correo sale igual, solo que sin logo o por el buzón equivocado. Este test es
/// lo que hace que el mapeador siga siendo el único camino cuando alguien agregue el consumer #31
/// copiando un ejemplo viejo.</para>
/// </summary>
public sealed class DispatchRequestMappingArchitectureTests
{
    private static readonly Regex ManualConstruction = new(@"new\s+EmailDispatchRequest\s*\(", RegexOptions.Compiled);

    [Fact]
    public void No_consumer_builds_the_dispatch_request_by_hand()
    {
        var offenders = ConsumerFiles()
            .Where(f => ManualConstruction.IsMatch(File.ReadAllText(f)))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Estos consumers arman el EmailDispatchRequest a mano en vez de usar ToDispatchRequest: "
                + string.Join(", ", offenders)
        );
    }

    [Fact]
    public void The_consumers_folder_is_where_this_test_thinks_it_is()
    {
        // Sin esto, mover o renombrar la carpeta dejaría el test anterior pasando sobre cero archivos:
        // verde para siempre y sin vigilar nada.
        Assert.NotEmpty(ConsumerFiles());
    }

    private static IReadOnlyList<string> ConsumerFiles()
    {
        var root = FindRepositoryRoot();
        var consumers = Path.Combine(
            root,
            "src",
            "Services",
            "Notification",
            "TaxVision.Notification.Application",
            "Consumers"
        );
        return Directory.Exists(consumers) ? Directory.GetFiles(consumers, "*.cs", SearchOption.AllDirectories) : [];
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("No se encontro la raiz del repositorio.");
    }
}
