using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Tenant.Application.Compliance.Commands;
using TaxVision.Tenant.Domain.Compliance;
using TaxVision.Tenant.Domain.ValueObjects;
using TaxVision.Tenant.Infrastructure.Security;
using TaxVision.Tenant.Tests.TestSupport;

namespace TaxVision.Tenant.Tests.Application;

/// <summary>POST tenants/current/compliance/efin/reveal: cada revelado queda registrado antes de responder.</summary>
public sealed class RevealOfficeEfinHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _actorId = Guid.NewGuid();
    private readonly InMemoryOfficeComplianceRepository _compliances = new();
    private readonly EfinProtector _protector = TestEfinProtectors.Create(out _);
    private readonly CountingUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task Returns_the_full_efin_and_records_who_saw_it()
    {
        var compliance = OfficeCompliance.CreateEmpty(_tenantId);
        compliance.Update(
            new OfficeComplianceDraft(null, null, null, null, null, null),
            EfinChange.Replace(_protector.Protect(Efin.Create("654321").Value)),
            Guid.NewGuid(),
            DateTime.UtcNow
        );
        _compliances.Compliances.Add(compliance);

        var first = await HandleAsync();
        var second = await HandleAsync();

        Assert.Equal("654321", first.Value.Efin);
        Assert.Equal("654321", second.Value.Efin);
        Assert.Equal(2, _compliances.RevealRecords.Count);
        Assert.All(_compliances.RevealRecords, record => Assert.Equal(_actorId, record.RevealedByUserId));
        Assert.All(_compliances.RevealRecords, record => Assert.Equal(_tenantId, record.TenantId));
        Assert.Equal(2, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Without_an_efin_it_is_not_found_and_nothing_is_recorded()
    {
        _compliances.Compliances.Add(OfficeCompliance.CreateEmpty(_tenantId));

        var withCompliance = await HandleAsync();
        _compliances.Compliances.Clear();
        var withoutCompliance = await HandleAsync();

        Assert.Equal(OfficeComplianceErrors.EfinNotSet, withCompliance.Error);
        Assert.Equal(OfficeComplianceErrors.EfinNotSet, withoutCompliance.Error);
        Assert.Empty(_compliances.RevealRecords);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    private Task<Result<RevealedEfinResponse>> HandleAsync() =>
        RevealOfficeEfinHandler.Handle(
            new RevealOfficeEfinCommand(_tenantId, _actorId),
            _compliances,
            _protector,
            _unitOfWork,
            NullLogger<RevealOfficeEfinCommand>.Instance,
            TimeProvider.System,
            CancellationToken.None
        );
}
