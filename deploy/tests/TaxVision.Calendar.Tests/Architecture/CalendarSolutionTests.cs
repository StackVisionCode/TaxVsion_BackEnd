using TaxVision.Calendar.Infrastructure.Persistence;
using Xunit;

namespace TaxVision.Calendar.Tests.Architecture;

/// <summary>
/// Los dos servicios Node del repo se desplegaron meses sin ejecutar un solo test. Esta clase existe
/// para que el proyecto entre de verdad en la suite desde el primer commit, no solo para que exista.
/// </summary>
public sealed class CalendarSolutionTests
{
    [Fact]
    public void The_design_time_factory_builds_a_context_without_infrastructure()
    {
        // Si el factory necesitara RabbitMQ o el JWT, `dotnet ef` no podria migrar.
        using var context = new CalendarDbContextFactory().CreateDbContext([]);

        Assert.NotNull(context.Model);
    }
}
