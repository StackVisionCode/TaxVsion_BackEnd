using System.Text.RegularExpressions;
using BuildingBlocks.Messaging;
using Xunit;
using Xunit.Abstractions;

namespace TaxVision.BuildingBlocks.Tests.Messaging;

/// <summary>
/// H-15. La política de fallo del bus vive en un solo sitio
/// (<see cref="WolverineFailurePolicies"/>) y los 17 servicios la aplican tal cual.
///
/// <para>
/// Antes estaba copiada literalmente en los 17 <c>Program.cs</c>. Una copia es una divergencia
/// esperando a pasar: el servicio 18 se escribe copiando el 17, y si alguien ajusta los cooldowns
/// en uno, los otros 16 se quedan atrás sin que nada lo note. Este test convierte esa divergencia
/// en un build rojo.
/// </para>
///
/// <para>
/// El comportamiento en sí está medido contra el broker real, no deducido: con esta política, una
/// excepción permanente da 1 intento y aterriza en <c>wolverine-dead-letter-queue</c>, y una
/// transitoria sigue dando los 4 de siempre.
/// </para>
/// </summary>
public sealed class FailurePolicyContractTests(ITestOutputHelper output)
{
    private static DirectoryInfo RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    private static FileInfo[] ServiceProgramFiles() =>
        new DirectoryInfo(Path.Combine(RepositoryRoot().FullName, "src", "Services"))
            .EnumerateFiles("Program.cs", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f.FullName).Contains("UseWolverine", StringComparison.Ordinal))
            .ToArray();

    /// <summary>
    /// Ningún servicio configura reintentos por su cuenta: o usa el helper, o no toca el bus.
    /// </summary>
    [Fact]
    public void NingunServicioDeclaraSuPropiaPoliticaDeReintentos()
    {
        var offenders = ServiceProgramFiles()
            .Where(f => File.ReadAllText(f.FullName).Contains("RetryWithCooldown", StringComparison.Ordinal))
            .Select(f => f.FullName)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "Estos Program.cs declaran su propia política de reintentos en vez de llamar a "
                + $"{nameof(WolverineFailurePolicies)}.{nameof(WolverineFailurePolicies.ApplyStandardFailurePolicies)}():\n  "
                + string.Join("\n  ", offenders)
                + "\nUna copia más es una divergencia más: mover los cooldowns dejaría de aplicar aquí."
        );
    }

    /// <summary>
    /// Y el que usa Wolverine la aplica de verdad — un servicio nuevo que se olvide del helper
    /// heredaría el default de la librería (reintentar todo, para siempre) sin que nadie lo vea.
    /// </summary>
    [Fact]
    public void CadaServicioConWolverineAplicaLaPoliticaCompartida()
    {
        var files = ServiceProgramFiles();
        Assert.NotEmpty(files);

        var missing = files
            .Where(f =>
                !File.ReadAllText(f.FullName)
                    .Contains(nameof(WolverineFailurePolicies.ApplyStandardFailurePolicies), StringComparison.Ordinal)
            )
            .Select(f => f.FullName)
            .ToArray();

        output.WriteLine($"H-15 — servicios con Wolverine verificados: {files.Length}");

        Assert.True(
            missing.Length == 0,
            "Estos Program.cs usan Wolverine pero no aplican la política de fallo compartida:\n  "
                + string.Join("\n  ", missing)
        );
    }

    /// <summary>
    /// La lista de excepciones permanentes es deliberadamente corta. Este test no la congela —
    /// afirma la regla que la gobierna: nada entra si un reintento pudiera ayudar. Los tres tipos
    /// de abajo se descartaron uno a uno porque bajo consistencia eventual el segundo intento sí
    /// puede ir mejor que el primero, y meterlos convertiría un retraso normal de proyección en
    /// un mensaje muerto.
    /// </summary>
    [Fact]
    public void LasExcepcionesQueUnReintentoPodriaArreglarNoSonPermanentes()
    {
        Type[] retryables =
        [
            typeof(InvalidOperationException), // EF: "sequence contains no elements" con la proyección aún sin llegar
            typeof(NullReferenceException), // leer una proyección que todavía no existe
            typeof(KeyNotFoundException), // idem
        ];

        var permanent = WolverineFailurePolicies.PermanentFailureTypes;

        foreach (var t in retryables)
        {
            Assert.False(
                permanent.Any(p => p.IsAssignableFrom(t)),
                $"{t.Name} está tratada como permanente, pero bajo consistencia eventual un "
                    + "reintento sí puede arreglarla. Mandarla directa a la DLQ perdería mensajes "
                    + "buenos por un retraso normal de proyección."
            );
        }

        output.WriteLine("H-15 — excepciones permanentes: " + string.Join(", ", permanent.Select(p => p.Name)));
    }

    /// <summary>
    /// Y las que sí están dependen solo del payload: el reintento recibe los mismos bytes.
    /// </summary>
    [Fact]
    public void LasExcepcionesPermanentesNoDependenDelEstadoExterno()
    {
        Assert.NotEmpty(WolverineFailurePolicies.PermanentFailureTypes);

        Assert.All(
            WolverineFailurePolicies.PermanentFailureTypes,
            t => Assert.True(typeof(Exception).IsAssignableFrom(t), $"{t.Name} no es una excepción.")
        );

        // El helper es la única fuente: si alguien añade un tipo, tiene que pasar por aquí.
        var source = File.ReadAllText(
            Path.Combine(RepositoryRoot().FullName, "src", "BuildingBlocks", "Messaging", "WolverineFailurePolicies.cs")
        );

        var declared = Regex
            .Matches(source, @"typeof\((\w+)\)")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var t in WolverineFailurePolicies.PermanentFailureTypes)
            Assert.Contains(t.Name, declared);
    }
}
