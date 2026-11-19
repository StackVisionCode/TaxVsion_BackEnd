using System.Text.RegularExpressions;
using BuildingBlocks.Contracts.Work;
using TaxVision.Tenant.Application.Team;

namespace TaxVision.Tenant.Tests.Architecture;

/// <summary>
/// El registro de proveedores del Work Profile (<see cref="TeamWorkProviders"/>) contra el código real: cada path
/// declarado existe como GET en su servicio (src/Services/&lt;Service&gt;, C# o Node) y todo <c>/work/{userId}</c>
/// real está registrado. Lee el fuente, sin referenciar los servicios (patrón de OffboardingParticipantsContractTests).
/// </summary>
public sealed class TeamWorkProvidersTests
{
    private static readonly Regex ClassRoute = new(@"\[Route\(\s*""(?<route>[^""]*)""\s*\)\]", RegexOptions.Compiled);
    private static readonly Regex CSharpGet = new(@"\[HttpGet\(\s*""(?<template>[^""]*)""", RegexOptions.Compiled);
    private static readonly Regex TsGet = new(@"\.get\(\s*['""`](?<path>/[\w/:-]+)['""`]", RegexOptions.Compiled);

    [Fact]
    public void Every_registered_path_exists_as_a_get_route_in_its_service()
    {
        var problems = PathProblems(TeamWorkProviders.All, LoadRoutes());

        Assert.True(problems.Count == 0, "Registro de proveedores sin ruta real:\n  " + string.Join("\n  ", problems));
    }

    [Fact]
    public void Every_real_work_endpoint_is_registered()
    {
        var registered = TeamWorkProviders.All.Select(p => p.WorkPath).ToHashSet(StringComparer.Ordinal);
        var unregistered = LoadRoutes()
            .SelectMany(s => s.Value.Select(path => (Service: s.Key, Path: path)))
            .Where(r => r.Path.EndsWith("/work/{userId}", StringComparison.Ordinal) && !registered.Contains(r.Path))
            .Select(r => $"{r.Service}: {r.Path}")
            .ToList();

        Assert.True(unregistered.Count == 0, "Proveedores sin registrar:\n  " + string.Join("\n  ", unregistered));
    }

    [Fact]
    public void Registered_entries_are_unique_and_carry_the_user_placeholder()
    {
        Assert.Equal(TeamWorkProviders.All.Count, TeamWorkProviders.All.Select(p => p.Service).Distinct().Count());
        Assert.All(TeamWorkProviders.All, p => Assert.EndsWith("/work/{userId}", p.WorkPath));
        Assert.All(TeamWorkProviders.All, p => Assert.EndsWith("/work/summary", p.BatchPath));
    }

    [Fact]
    public void Flags_a_path_that_does_not_exist_or_a_service_that_does_not_exist()
    {
        var routes = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Tasks"] = RoutesOf(
                "Tasks/WorkController.cs",
                """
                [Route("tasks/work")]
                public sealed class WorkController
                {
                    [HttpGet("{userId:guid}")] public void Work() { }
                    [HttpGet("summary")] public void Summary() { }
                }
                """
            ),
            ["Communication"] = RoutesOf(
                "Communication/src/api/work.route.ts",
                "app.get('/communication/work/:userId', handler); app.get('/communication/work/summary', h);"
            ),
        };

        var problems = PathProblems(
            [
                new("tasks", "planner", "/tasks/work/{userId}", "/tasks/work/summary"),
                new("communication", "comms", "/communication/work/{userId}", "/communication/work/summary"),
                new("tasks", "planner", "/tasks/work/{userId}", "/tasks/work/batch"),
                new("ghost", "x", "/ghost/work/{userId}", "/ghost/work/summary"),
            ],
            routes
        );

        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Contains("/tasks/work/batch"));
        Assert.Contains(problems, p => p.Contains("ghost"));
    }

    private static List<string> PathProblems(
        IReadOnlyList<WorkProviderDescriptor> registry,
        IReadOnlyDictionary<string, HashSet<string>> routes
    )
    {
        var problems = new List<string>();
        foreach (var provider in registry)
        {
            if (!routes.TryGetValue(provider.Service, out var paths))
            {
                problems.Add($"{provider.Service}: no existe src/Services/{provider.Service}");
                continue;
            }

            foreach (var path in new[] { provider.WorkPath, provider.BatchPath }.Where(p => !paths.Contains(p)))
                problems.Add($"{provider.Service}: {path} no existe como GET");
        }

        return problems;
    }

    private static Dictionary<string, HashSet<string>> LoadRoutes()
    {
        var services = Path.Combine(RepositoryRoot(), "src", "Services");
        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var serviceDir in Directory.EnumerateDirectories(services))
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in Directory.EnumerateFiles(serviceDir, "*.*", SearchOption.AllDirectories))
            {
                if (IsSource(file))
                    paths.UnionWith(RoutesOf(file, File.ReadAllText(file)));
            }

            result[Path.GetFileName(serviceDir)] = paths;
        }

        return result;
    }

    private static HashSet<string> RoutesOf(string file, string text)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        if (file.EndsWith(".cs", StringComparison.Ordinal))
        {
            foreach (Match get in CSharpGet.Matches(text))
            {
                var template = get.Groups["template"].Value;
                var classRoute = ClassRoute.Matches(text[..get.Index]).LastOrDefault()?.Groups["route"].Value;
                paths.Add(Normalize(template.StartsWith('/') ? template : $"/{classRoute?.Trim('/')}/{template}"));
            }
        }
        else
        {
            foreach (Match get in TsGet.Matches(text))
                paths.Add(Normalize(get.Groups["path"].Value));
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
