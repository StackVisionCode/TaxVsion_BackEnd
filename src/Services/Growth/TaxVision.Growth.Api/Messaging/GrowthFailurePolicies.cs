using TaxVision.Growth.Api.IntegrationEvents;
using Wolverine;
using Wolverine.ErrorHandling;

namespace TaxVision.Growth.Api.Messaging;

/// <summary>Políticas de fallo propias de Growth; van antes de ApplyStandardFailurePolicies.</summary>
public static class GrowthFailurePolicies
{
    public static WolverineOptions ApplyGrowthFailurePolicies(this WolverineOptions options)
    {
        // Regla de negocio fija (GRW-1): sin reintentos, directo a la cola de errores.
        options.Policies.OnException<GrowthBusinessRuleException>().MoveToErrorQueue();
        return options;
    }
}
