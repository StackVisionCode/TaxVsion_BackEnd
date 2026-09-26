using System.Text.RegularExpressions;
using TaxVision.Auth.Domain.Roles;

namespace TaxVision.Auth.Tests.Architecture;

/// <summary>
/// A4.4 — fitness function: todo código que algún endpoint exige con <c>[HasPermission]</c> o
/// <c>[HasPermissionForActor]</c> tiene que existir en <see cref="PermissionCatalog"/>.
///
/// <para>
/// El motivo es un bug real y repetido: <c>signature.constraints.manage</c> y los dos de
/// <c>connectors.accounts.*</c> se exigían desde que se construyeron sus controllers pero nunca se
/// habían sembrado. Sin fila en el catálogo ningún rol puede tenerlos, así que el endpoint dependía
/// por completo del bypass por nombre de rol — y cuando ese bypass se retiró (A0), el endpoint quedó
/// inalcanzable para todos. Un chequeo de catálogo en CI lo habría atrapado el día que se escribió.
/// </para>
///
/// <para>
/// Es un escaneo de texto sobre <c>src/</c>, no un análisis de C#: resuelve las constantes
/// <c>public const string</c> de todo el repo para poder seguir <c>[HasPermission(XPermissions.Y)]</c>
/// hasta su literal. Los 26 microservicios declaran sus permisos en su propia clase, así que este
/// proyecto de test no puede referenciarlos por assembly sin crear dependencias cruzadas entre
/// servicios.
/// </para>
///
/// <para>
/// La dirección inversa (todo código del catálogo se exige en algún lado o es
/// <see cref="Permission.IsReserved"/>) queda para A7: hay ~60 códigos que se exigen fuera de un
/// atributo —Communication los chequea en Node y varios servicios llaman al chequeo de forma
/// imperativa—, así que medirla con este escaneo daría falsos positivos.
/// </para>
/// </summary>
public sealed class EnforcedPermissionsFitnessTests
{
    [Fact]
    public void Every_permission_code_required_by_an_endpoint_exists_in_the_catalog()
    {
        var sourceRoot = Path.Combine(RepoRoot(), "src");
        var constants = CollectStringConstants(sourceRoot);
        var enforced = CollectEnforcedCodes(sourceRoot, constants);

        Assert.NotEmpty(enforced);

        var catalog = PermissionCatalog
            .All.Select(definition => definition.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = enforced.Where(code => !catalog.Contains(code)).OrderBy(code => code).ToArray();

        Assert.True(
            missing.Length == 0,
            "Estos permisos los exige un endpoint pero no existen en PermissionCatalog, así que ningún "
                + "rol puede tenerlos y el endpoint es inalcanzable: "
                + string.Join(", ", missing)
        );
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static IEnumerable<string> SourceFiles(string sourceRoot) =>
        Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal
                )
                && !path.Contains(
                    $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal
                )
            );

    /// <summary>Quita comentarios para no leer un <c>[HasPermission("signature.*")]</c> de ejemplo
    /// escrito en un comentario del composition root como si fuera un atributo real.</summary>
    private static string StripComments(string text) =>
        Regex.Replace(
            Regex.Replace(text, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline),
            @"//[^\n]*",
            string.Empty
        );

    /// <summary>Mapa "Clase.Miembro" → expresión declarada, para todas las <c>const string</c> del repo.</summary>
    private static Dictionary<string, string> CollectStringConstants(string sourceRoot)
    {
        var constants = new Dictionary<string, string>(StringComparer.Ordinal);
        var typePattern = new Regex(@"\b(?:class|record|struct)\s+(\w+)");
        var constPattern = new Regex(@"const\s+string\s+(\w+)\s*=\s*(.+?);");

        foreach (var path in SourceFiles(sourceRoot))
        {
            string? declaringType = null;
            foreach (var line in StripComments(File.ReadAllText(path)).Split('\n'))
            {
                var typeMatch = typePattern.Match(line);
                if (typeMatch.Success)
                    declaringType = typeMatch.Groups[1].Value;

                var constMatch = constPattern.Match(line);
                if (constMatch.Success && declaringType is not null)
                {
                    var key = $"{declaringType}.{constMatch.Groups[1].Value}";
                    // El primero gana: si dos servicios declaran el mismo nombre de clase y miembro,
                    // el valor es el mismo código de permiso en la práctica (espejos del catálogo).
                    constants.TryAdd(key, constMatch.Groups[2].Value.Trim());
                }
            }
        }

        return constants;
    }

    private static string? Resolve(string expression, Dictionary<string, string> constants, int depth = 0)
    {
        var trimmed = expression.Trim();
        if (depth > 8 || trimmed.Length == 0)
            return null;

        if (trimmed.StartsWith('"') && trimmed.EndsWith('"'))
            return trimmed.Trim('"');

        var segments = trimmed.Split('.');
        var candidates = new[]
        {
            trimmed,
            segments.Length >= 2 ? string.Join('.', segments[^2..]) : trimmed,
            "PermissionCatalog." + trimmed,
        };

        foreach (var candidate in candidates)
        {
            if (constants.TryGetValue(candidate, out var declared))
                return Resolve(declared, constants, depth + 1);
        }

        return null;
    }

    private static HashSet<string> CollectEnforcedCodes(string sourceRoot, Dictionary<string, string> constants)
    {
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var attributePattern = new Regex(@"\[HasPermission(?:ForActor)?\(([^)\]]*)\)");

        foreach (var path in SourceFiles(sourceRoot))
        {
            foreach (Match match in attributePattern.Matches(StripComments(File.ReadAllText(path))))
            {
                // [HasPermissionForActor(actorType, code)] — el código es siempre el último argumento.
                var lastArgument = match.Groups[1].Value.Split(',')[^1];
                var resolved = Resolve(lastArgument, constants);
                if (resolved is not null)
                    codes.Add(resolved);
            }
        }

        return codes;
    }
}
