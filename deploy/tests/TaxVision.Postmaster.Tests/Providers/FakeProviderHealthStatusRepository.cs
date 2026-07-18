using BuildingBlocks.Results;
using TaxVision.Postmaster.Application.Providers;
using TaxVision.Postmaster.Domain.Providers;

namespace TaxVision.Postmaster.Tests.Providers;

internal sealed class FakeProviderHealthStatusRepository : IProviderHealthStatusRepository
{
    private readonly List<ProviderHealthStatus> _statuses = [];

    public Task AddAsync(ProviderHealthStatus status, CancellationToken ct = default)
    {
        _statuses.Add(status);
        return Task.CompletedTask;
    }

    public Task<Result<ProviderHealthStatus>> GetAsync(
        ProviderKind providerKind,
        Guid? tenantId,
        string providerCode,
        CancellationToken ct = default
    )
    {
        var status = _statuses.Find(s =>
            s.ProviderKind == providerKind && s.TenantId == tenantId && s.ProviderCode == providerCode
        );
        return Task.FromResult(
            status is null
                ? Result.Failure<ProviderHealthStatus>(new Error("ProviderHealthStatus.NotFound", "Not found."))
                : Result.Success(status)
        );
    }
}
