using BuildingBlocks.TenantTimeZones;
using BuildingBlocks.TimeZones;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.TenantTimeZones;

/// <summary>Las piezas de zona horaria son de BuildingBlocks: no pueden depender de ningún servicio (P16).</summary>
public sealed class TenantTimeZonesDependencyTests
{
    [Fact]
    public void Time_zone_pieces_do_not_reference_any_service()
    {
        var assemblies = new[] { typeof(TenantTimeZoneProjection).Assembly, typeof(CivilDate).Assembly };

        var serviceReferences = assemblies
            .SelectMany(a => a.GetReferencedAssemblies())
            .Where(r => r.Name is not null && r.Name.StartsWith("TaxVision.", StringComparison.Ordinal))
            .Select(r => r.Name)
            .ToList();

        Assert.Empty(serviceReferences);
    }
}
