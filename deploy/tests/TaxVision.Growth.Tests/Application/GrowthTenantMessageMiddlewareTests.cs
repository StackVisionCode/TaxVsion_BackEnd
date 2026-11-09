using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.PaymentIntegrationEvents;
using BuildingBlocks.Web.Tenancy;
using TaxVision.Growth.Api.Common;

namespace TaxVision.Growth.Tests.Application;

public sealed class GrowthTenantMessageMiddlewareTests
{
    // ArgumentException va directo a la cola de errores: el mismo payload no se arregla reintentando.
    [Fact]
    public void Before_EmptyTenant_ThrowsArgumentException()
    {
        Assert.Contains(typeof(ArgumentException), WolverineFailurePolicies.PermanentFailureTypes);

        Assert.Throws<ArgumentException>(() =>
            GrowthTenantMessageMiddleware.Before(NewEvent(Guid.Empty), new TenantContext())
        );
    }

    [Fact]
    public void Before_WithTenant_SetsTheTenant()
    {
        var tenantId = Guid.NewGuid();
        var context = new TenantContext();

        GrowthTenantMessageMiddleware.Before(NewEvent(tenantId), context);

        Assert.Equal(tenantId, context.TenantId);
    }

    private static PaymentCancelledIntegrationEvent NewEvent(Guid tenantId) =>
        new()
        {
            TenantId = tenantId,
            AggregateId = Guid.NewGuid(),
            AggregateVersion = 1,
            PaymentSource = "PaymentApp",
            PaymentId = Guid.NewGuid(),
            GrossAmountCents = 100,
            DiscountAmountCents = 0,
            NetAmountCents = 100,
            Currency = "USD",
            ReasonCode = "cancelled",
            CancelledAtUtc = DateTime.UtcNow,
        };
}
