using BuildingBlocks.Messaging;
using BuildingBlocks.Tenancy;

namespace TaxVision.Growth.Api.Common;

/// <summary>
/// Establece el tenant del scope de un consumidor desde el contrato validado. Los
/// productores actuales no siempre copian TenantId al metadata nativo de Wolverine,
/// por lo que el payload es la fuente explícita y un GUID vacío falla cerrado.
/// </summary>
public static class GrowthTenantMessageMiddleware
{
    public static void Before(IIntegrationEvent message, TenantContext tenantContext)
    {
        if (message.TenantId == Guid.Empty)
            throw new InvalidOperationException($"Message {message.GetType().Name} does not contain a valid TenantId.");

        tenantContext.SetTenant(message.TenantId);
    }
}
