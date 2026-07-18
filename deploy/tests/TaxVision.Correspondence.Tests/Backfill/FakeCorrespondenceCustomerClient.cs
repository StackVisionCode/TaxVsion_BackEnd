using BuildingBlocks.Common;
using TaxVision.Correspondence.Application.Abstractions;

namespace TaxVision.Correspondence.Tests.Backfill;

/// <summary>Doble en memoria de Customer.Api — sirve páginas fijas de <see cref="RemoteCustomerSummary"/>
/// por tenant, o simula una falla de red devolviendo null.</summary>
internal sealed class FakeCorrespondenceCustomerClient : ICorrespondenceCustomerClient
{
    private readonly Dictionary<Guid, List<RemoteCustomerSummary>> _customersByTenant = [];
    private readonly int _pageSize;
    private bool _failNextCall;

    public FakeCorrespondenceCustomerClient(int pageSize = 100) => _pageSize = pageSize;

    public List<Guid> RequestedPages { get; } = [];

    public void Seed(Guid tenantId, params RemoteCustomerSummary[] customers)
    {
        if (!_customersByTenant.TryGetValue(tenantId, out var list))
            _customersByTenant[tenantId] = list = [];
        list.AddRange(customers);
    }

    public void FailNextCall() => _failNextCall = true;

    public Task<PagedResult<RemoteCustomerSummary>?> ListActiveCustomersAsync(
        Guid tenantId,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        RequestedPages.Add(tenantId);

        if (_failNextCall)
        {
            _failNextCall = false;
            return Task.FromResult<PagedResult<RemoteCustomerSummary>?>(null);
        }

        var all = _customersByTenant.GetValueOrDefault(tenantId, []);
        var items = all.Skip((page - 1) * _pageSize).Take(_pageSize).ToList();
        return Task.FromResult<PagedResult<RemoteCustomerSummary>?>(
            new PagedResult<RemoteCustomerSummary>(items, page, _pageSize, all.Count)
        );
    }
}
