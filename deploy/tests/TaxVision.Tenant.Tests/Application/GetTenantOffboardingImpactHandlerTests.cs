using TaxVision.Tenant.Application.Compliance.Queries;
using TaxVision.Tenant.Domain.Compliance;
using TaxVision.Tenant.Tests.TestSupport;

namespace TaxVision.Tenant.Tests.Application;

/// <summary>Pre-flight del retiro en Tenant: ¿el empleado es el responsable de seguridad de su oficina?</summary>
public sealed class GetTenantOffboardingImpactHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _coordinatorId = Guid.NewGuid();
    private readonly InMemoryOfficeComplianceRepository _compliances = new();

    [Fact]
    public async Task Only_the_designated_coordinator_of_the_same_office_is_flagged()
    {
        var compliance = OfficeCompliance.CreateEmpty(_tenantId);
        compliance.Update(
            new OfficeComplianceDraft(_coordinatorId, null, null, null, null, null),
            EfinChange.Keep,
            Guid.NewGuid(),
            DateTime.UtcNow
        );
        _compliances.Compliances.Add(compliance);

        Assert.True((await HandleAsync(_tenantId, _coordinatorId)).IsSecurityCoordinator);
        Assert.False((await HandleAsync(_tenantId, Guid.NewGuid())).IsSecurityCoordinator);
        Assert.False((await HandleAsync(Guid.NewGuid(), _coordinatorId)).IsSecurityCoordinator);
    }

    [Fact]
    public async Task An_office_without_compliance_has_no_coordinator()
    {
        Assert.False((await HandleAsync(_tenantId, Guid.Empty)).IsSecurityCoordinator);
        Assert.False((await HandleAsync(_tenantId, _coordinatorId)).IsSecurityCoordinator);
    }

    private Task<TenantOffboardingImpactResponse> HandleAsync(Guid tenantId, Guid userId) =>
        GetTenantOffboardingImpactHandler.Handle(
            new GetTenantOffboardingImpactQuery(tenantId, userId),
            _compliances,
            CancellationToken.None
        );
}
