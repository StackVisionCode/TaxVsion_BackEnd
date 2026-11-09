using TaxVision.Growth.Infrastructure.Persistence;
using Wolverine;

namespace TaxVision.Growth.Tests.Architecture;

public sealed class GrowthArchitectureTests
{
    // Publicar desde el DbContext va en memoria, fuera de la transacción del comando (GRW-2).
    [Fact]
    public void DbContext_DoesNotDependOnIMessageBus()
    {
        var parameters = typeof(GrowthDbContext)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType);

        Assert.DoesNotContain(typeof(IMessageBus), parameters);
    }
}
