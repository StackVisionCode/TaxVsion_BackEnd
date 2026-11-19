using System.Reflection;
using BuildingBlocks.Contracts.Work;
using TaxVision.Tenant.Application.Team;

namespace TaxVision.Tenant.Tests.Architecture;

/// <summary>
/// Registro de títulos del historial (<see cref="TeamTitleProviders"/>) contra el código real: cada path existe como
/// POST en su servicio y entre todos cubren cada tipo de recurso, salvo los que a propósito no llevan título.
/// </summary>
public sealed class TeamTitleProvidersTests
{
    // El nombre del cliente lo resuelve el CRM con su directorio; el contenido de un SMS no se muestra.
    private static readonly string[] WithoutTitle = [WorkResourceTypes.Customer, WorkResourceTypes.SmsMessage];

    [Fact]
    public void Every_registered_path_exists_as_a_post_route_in_its_service()
    {
        var routes = ServiceRouteScanner.Load("Post");
        var problems = TeamTitleProviders
            .All.Where(p => !routes.TryGetValue(p.Service, out var paths) || !paths.Contains(p.Path))
            .Select(p => $"{p.Service}: {p.Path}")
            .ToList();

        Assert.True(problems.Count == 0, "Títulos sin ruta POST real:\n  " + string.Join("\n  ", problems));
    }

    [Fact]
    public void Every_resource_type_with_a_title_has_exactly_one_provider()
    {
        var expected = typeof(WorkResourceTypes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (string)f.GetRawConstantValue()!)
            .Except(WithoutTitle)
            .Order()
            .ToList();
        var registered = TeamTitleProviders.All.SelectMany(p => p.ResourceTypes).ToList();

        Assert.Equal(registered.Count, registered.Distinct().Count());
        Assert.Equal(expected, registered.Order().ToList());
    }

    [Fact]
    public void Paths_follow_the_work_titles_convention()
    {
        Assert.Equal(TeamTitleProviders.All.Count, TeamTitleProviders.All.Select(p => p.Service).Distinct().Count());
        Assert.All(TeamTitleProviders.All, p => Assert.EndsWith("/work/titles", p.Path));
    }
}
