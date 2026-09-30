using System.Text.RegularExpressions;
using Xunit;

namespace TaxVision.Auth.Tests.Architecture;

/// <summary>
/// Lee el repo entero para responder dos preguntas sobre los permisos: qué exige cada endpoint y qué
/// código se usa en algún lado. Se hace por fuente y no por reflexión porque los 26 microservicios
/// declaran sus permisos en su propia clase y este proyecto de test no puede referenciarlos por
/// assembly sin crear dependencias cruzadas entre servicios.
/// </summary>
internal static class PermissionSourceIndex
{
    /// <summary>
    /// Archivos que solo DECLARAN o COMPONEN permisos: el catálogo (con sus alias y los bundles de los
    /// roles de sistema), las clases de constantes y el espejo de Node. Aparecer acá no es usar un
    /// permiso — si contaran, un código muerto se justificaría a sí mismo.
    /// </summary>
    private static readonly string[] DeclarationOnly =
    [
        Path.Combine("Auth", "Domain", "Roles", "PermissionCatalog.cs"),
        Path.Combine("BuildingBlocks", "Authorization") + Path.DirectorySeparatorChar,
        "PermissionModuleMap.cs",
        Path.Combine("domain", "shared", "permissions.ts"),
    ];

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    internal static IEnumerable<string> SourceFiles(string sourceRoot, string extension = "*.cs") =>
        Directory
            .EnumerateFiles(sourceRoot, extension, SearchOption.AllDirectories)
            .Where(path => !Contains(path, "obj") && !Contains(path, "bin") && !Contains(path, "node_modules"));

    private static bool Contains(string path, string segment) =>
        path.Contains($"{Path.DirectorySeparatorChar}{segment}{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static bool IsDeclarationOnly(string path) =>
        DeclarationOnly.Any(marker => path.Contains(marker, StringComparison.OrdinalIgnoreCase));

    /// <summary>Quita comentarios para no leer un <c>[HasPermission("signature.*")]</c> de ejemplo
    /// escrito en un comentario del composition root como si fuera un atributo real.</summary>
    internal static string StripComments(string text) =>
        Regex.Replace(
            Regex.Replace(text, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline),
            @"//[^\n]*",
            string.Empty
        );

    /// <summary>Mapa "Clase.Miembro" → expresión declarada, para todas las <c>const string</c> del repo.</summary>
    internal static Dictionary<string, string> CollectStringConstants(string sourceRoot)
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

    /// <summary>
    /// El espejo de Node declara los mismos códigos con OTRO nombre de clase
    /// (<c>CommunicationPermissions.PortalCallsUse</c>), así que sin esto un permiso que solo se exige
    /// en Communication parecería muerto. Se indexa igual que una constante de C#.
    /// </summary>
    internal static void AddNodeMirrorConstants(string sourceRoot, Dictionary<string, string> constants)
    {
        var mirror = Path.Combine(sourceRoot, "Services", "Communication", "src", "domain", "shared", "permissions.ts");
        if (!File.Exists(mirror))
            return;

        var entryPattern = new Regex(@"^\s*(\w+):\s*'([a-z0-9_.]+)'", RegexOptions.Multiline);
        foreach (Match match in entryPattern.Matches(StripComments(File.ReadAllText(mirror))))
            constants.TryAdd($"CommunicationPermissions.{match.Groups[1].Value}", $"\"{match.Groups[2].Value}\"");
    }

    internal static string? Resolve(string expression, Dictionary<string, string> constants, int depth = 0)
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

    internal static HashSet<string> CollectEnforcedCodes(string sourceRoot, Dictionary<string, string> constants)
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

    /// <summary>
    /// Todo lo que se USA en algún lado, mirando C# y TypeScript. Un permiso se exige de tres formas y
    /// solo una es un atributo: el chequeo imperativo (<c>HasPermissionAsync(User, X.Y, ct)</c>) y el de
    /// Node (<c>CommunicationPermissions.ChatStart</c>) pasan por el nombre de la constante, no por el
    /// literal. Por eso se buscan referencias "Clase.Miembro" — la línea que DECLARA la constante no
    /// contiene esa forma, así que una declaración nunca se cuenta como uso.
    /// </summary>
    internal static HashSet<string> CollectReferencedMembers(string sourceRoot)
    {
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        var memberPattern = new Regex(@"\b([A-Z]\w*)\.([A-Z]\w*)\b");

        foreach (var extension in new[] { "*.cs", "*.ts" })
        {
            foreach (var path in SourceFiles(sourceRoot, extension))
            {
                if (IsDeclarationOnly(path))
                    continue;

                foreach (Match match in memberPattern.Matches(StripComments(File.ReadAllText(path))))
                    referenced.Add($"{match.Groups[1].Value}.{match.Groups[2].Value}");
            }
        }

        return referenced;
    }
}
