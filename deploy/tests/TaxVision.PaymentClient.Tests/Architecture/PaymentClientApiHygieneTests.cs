using TaxVision.PaymentClient.Api.Common;

namespace TaxVision.PaymentClient.Tests.Architecture;

/// <summary>El Api usa el middleware de tenant de BuildingBlocks: no debe volver una copia local.</summary>
public sealed class PaymentClientApiHygieneTests
{
    [Fact]
    public void NoLocalJwtTenantContextMiddleware()
    {
        var localCopies = typeof(TenantStatusGateMiddleware)
            .Assembly.GetTypes()
            .Where(t => t.Name == "JwtTenantContextMiddleware")
            .Select(t => t.FullName);

        Assert.Empty(localCopies);
    }
}
