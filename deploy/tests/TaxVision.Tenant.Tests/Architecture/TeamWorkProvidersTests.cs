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
    [Fact]
    public void Every_registered_path_exists_as_a_get_route_in_its_service()
    {
        var problems = PathProblems(TeamWorkProviders.All, ServiceRouteScanner.Load("Get"));

        Assert.True(problems.Count == 0, "Registro de proveedores sin ruta real:\n  " + string.Join("\n  ", problems));
    }

    [Fact]
    public void Every_real_work_endpoint_is_registered()
    {
        var registered = TeamWorkProviders
            .All.SelectMany(p => new[] { p.WorkPath, p.ItemsPath })
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var unregistered = ServiceRouteScanner
            .Load("Get")
            .SelectMany(s => s.Value.Select(path => (Service: s.Key, Path: path)))
            .Where(r =>
                (
                    r.Path.EndsWith("/work/{userId}", StringComparison.Ordinal)
                    || r.Path.EndsWith("/work/{userId}/items", StringComparison.Ordinal)
                ) && !registered.Contains(r.Path)
            )
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
        Assert.All(
            TeamWorkProviders.All.Where(p => p.ItemsPath is not null),
            p => Assert.Equal(p.WorkPath + "/items", p.ItemsPath)
        );
    }

    // Clients ya tiene su propia pestaña paginada; el resto ofrece "Show all" dentro del bloque.
    [Fact]
    public void Every_provider_but_customer_offers_the_paged_list()
    {
        Assert.All(TeamWorkProviders.All, p => Assert.Equal(p.Service != "customer", p.ItemsPath is not null));
    }

    [Fact]
    public void Flags_a_path_that_does_not_exist_or_a_service_that_does_not_exist()
    {
        var routes = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Tasks"] = ServiceRouteScanner.RoutesOf(
                "Tasks/WorkController.cs",
                """
                [Route("tasks/work")]
                public sealed class WorkController
                {
                    [HttpGet("{userId:guid}")] public void Work() { }
                    [HttpGet("summary")] public void Summary() { }
                }
                """,
                "Get"
            ),
            ["Communication"] = ServiceRouteScanner.RoutesOf(
                "Communication/src/api/work.route.ts",
                "app.get('/communication/work/:userId', handler); app.get('/communication/work/summary', h);",
                "Get"
            ),
        };

        var problems = PathProblems(
            [
                new("tasks", "planner", "/tasks/work/{userId}", "/tasks/work/summary"),
                new("communication", "comms", "/communication/work/{userId}", "/communication/work/summary"),
                new("tasks", "planner", "/tasks/work/{userId}", "/tasks/work/batch"),
                new("tasks", "planner", "/tasks/work/{userId}", "/tasks/work/summary", "/tasks/work/{userId}/items"),
                new("ghost", "x", "/ghost/work/{userId}", "/ghost/work/summary"),
            ],
            routes
        );

        Assert.Equal(3, problems.Count);
        Assert.Contains(problems, p => p.Contains("/tasks/work/batch"));
        Assert.Contains(problems, p => p.Contains("/tasks/work/{userId}/items"));
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

            var declared = new[] { provider.WorkPath, provider.BatchPath, provider.ItemsPath }.OfType<string>();
            foreach (var path in declared.Where(p => !paths.Contains(p)))
                problems.Add($"{provider.Service}: {path} no existe como GET");
        }

        return problems;
    }
}
