using System.Text.RegularExpressions;

namespace TaxVision.Tenant.Tests.Architecture;

/// <summary>
/// Lee del fuente las rutas GET/POST de cada servicio (src/Services/&lt;Service&gt;, C# o Node) sin referenciarlos.
/// Lo usan los registros del Work Profile para comprobar que cada path declarado existe de verdad.
/// </summary>
internal static class ServiceRouteScanner
{
    private static readonly Regex ClassRoute = new(@"\[Route\(\s*""(?<route>[^""]*)""\s*\)\]", RegexOptions.Compiled);

    public static Dictionary<string, HashSet<string>> Load(string verb)
    {
        var services = Path.Combine(RepositoryRoot(), "src", "Services");
        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var serviceDir in Directory.EnumerateDirectories(services))
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in Directory.EnumerateFiles(serviceDir, "*.*", SearchOption.AllDirectories))
            {
                if (IsSource(file))
                    paths.UnionWith(RoutesOf(file, File.ReadAllText(file), verb));
            }

            result[Path.GetFileName(serviceDir)] = paths;
        }

        return result;
    }

    public static HashSet<string> RoutesOf(string file, string text, string verb)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        if (file.EndsWith(".cs", StringComparison.Ordinal))
        {
            var attribute = new Regex($@"\[Http{verb}\(\s*""(?<template>[^""]*)""");
            foreach (Match match in attribute.Matches(text))
            {
                var template = match.Groups["template"].Value;
                var classRoute = ClassRoute.Matches(text[..match.Index]).LastOrDefault()?.Groups["route"].Value;
                paths.Add(Normalize(template.StartsWith('/') ? template : $"/{classRoute?.Trim('/')}/{template}"));
            }
        }
        else
        {
            var call = new Regex($@"\.{verb.ToLowerInvariant()}\(\s*['""`](?<path>/[\w/:-]+)['""`]");
            foreach (Match match in call.Matches(text))
                paths.Add(Normalize(match.Groups["path"].Value));
        }

        return paths;
    }

    private static string Normalize(string path) =>
        Regex
            .Replace(Regex.Replace(path, @"\{userId(?::\w+)?\}", "{userId}"), ":userId", "{userId}")
            .Replace("//", "/");

    private static bool IsSource(string path)
    {
        var sep = Path.DirectorySeparatorChar;
        var excluded = new[] { "bin", "obj", "node_modules", "Migrations", "Generated", "tests", "dist" };
        return (path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".ts", StringComparison.Ordinal))
            && !excluded.Any(dir => path.Contains($"{sep}{dir}{sep}", StringComparison.Ordinal));
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
