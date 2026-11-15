using System.Reflection;
using JasperFx.CodeGeneration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Wolverine;

namespace BuildingBlocks.Web.Hosting;

/// <summary>
/// Código de los handlers pregenerado en producción. En modo Dynamic, Wolverine compila cada handler con Roslyn
/// la primera vez que se usa: cientos de MB nativos por compilación que glibc no devuelve en Linux. En Static
/// carga los tipos ya compilados en el Api (<c>Internal/Generated</c>; los regenera <c>scripts/codegen</c>).
/// Opcional: solo cambia el servicio que la llama.
/// </summary>
public static class WolverineCodeGenerationExtensions
{
    /// <summary>Fuerza el modo (Static, Dynamic o Auto); sirve para probar Static en local.</summary>
    public const string TypeLoadModeKey = "Wolverine:CodeGeneration:TypeLoadMode";

    public static WolverineOptions UseTaxVisionCodeGeneration(
        this WolverineOptions options,
        IHostEnvironment environment,
        IConfiguration configuration,
        Assembly applicationAssembly
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(applicationAssembly);
        // El código generado vive en el Api, no en los Application donde están los handlers.
        options.ApplicationAssembly = applicationAssembly;
        options.CodeGeneration.TypeLoadMode = ResolveTypeLoadMode(environment, configuration);
        return options;
    }

    /// <summary>Static solo en Production; el resto (Development, Testing) sigue compilando al vuelo.</summary>
    public static TypeLoadMode ResolveTypeLoadMode(IHostEnvironment environment, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(configuration);
        var forced = configuration[TypeLoadModeKey];
        if (!string.IsNullOrWhiteSpace(forced))
            return Enum.Parse<TypeLoadMode>(forced, ignoreCase: true);
        return environment.IsProduction() ? TypeLoadMode.Static : TypeLoadMode.Dynamic;
    }
}
