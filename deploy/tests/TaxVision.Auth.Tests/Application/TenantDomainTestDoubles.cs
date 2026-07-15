using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.TenantDomains;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Fakes compartidos por los tests de comandos de TenantDomains (Fase A5).</summary>
internal sealed class FakeTenantDomainRepository : ITenantDomainRepository
{
    private readonly Dictionary<Guid, TenantDomain> _byId = [];

    public void Seed(TenantDomain domain) => _byId[domain.Id] = domain;

    public TenantDomain? Added { get; private set; }

    public Task<TenantDomain?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_byId.GetValueOrDefault(id));

    public Task<TenantDomain?> GetByHostAsync(string host, CancellationToken ct = default) =>
        Task.FromResult(_byId.Values.FirstOrDefault(domain => domain.Host == host));

    public Task<IReadOnlyList<TenantDomain>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TenantDomain>>(
            _byId.Values.Where(domain => domain.TenantId == tenantId).ToList()
        );

    public Task<IReadOnlyList<string>> GetActiveHostsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public bool HostTaken { get; set; }

    public Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default) => Task.FromResult(false);

    public Task<bool> HostExistsAsync(string host, CancellationToken ct = default) => Task.FromResult(HostTaken);

    public Task AddAsync(TenantDomain domain, CancellationToken ct = default)
    {
        Added = domain;
        _byId[domain.Id] = domain;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TenantDomain>> GetProvisioningCustomHostnamesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TenantDomain>>(
            _byId.Values.Where(domain => domain.Status == TenantDomainStatus.Provisioning).ToList()
        );
}

internal sealed class FakeCloudflareProvisioningClient : ICloudflareProvisioningClient
{
    public Result<CustomHostnameResult> CreateResult { get; set; } =
        Result.Success(new CustomHostnameResult("cf-1", "pending", "pending", "_cf-verify", "abc123", []));

    public Result<CustomHostnameResult> GetResult { get; set; } =
        Result.Success(new CustomHostnameResult("cf-1", "active", "active", null, null, []));

    public bool DeleteCalled { get; private set; }
    public Result DeleteResult { get; set; } = Result.Success();

    public Task<Result<CustomHostnameResult>> CreateCustomHostnameAsync(
        string hostname,
        CancellationToken ct = default
    ) => Task.FromResult(CreateResult);

    public Task<Result<CustomHostnameResult>> GetCustomHostnameAsync(
        string cloudflareId,
        CancellationToken ct = default
    ) => Task.FromResult(GetResult);

    public Task<Result> DeleteCustomHostnameAsync(string cloudflareId, CancellationToken ct = default)
    {
        DeleteCalled = true;
        return Task.FromResult(DeleteResult);
    }
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveChangesCallCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        SaveChangesCallCount++;
        return Task.FromResult(1);
    }
}

internal sealed class FakeAuthAuditWriter : IAuthAuditWriter
{
    public List<AuthAuditLog> Logs { get; } = [];

    public Task AddAsync(AuthAuditLog log, CancellationToken ct = default)
    {
        Logs.Add(log);
        return Task.CompletedTask;
    }
}

internal sealed class FakeRequestContext : IRequestContext
{
    public string? IpAddress => "127.0.0.1";
    public string? UserAgent => "xunit";
}

internal sealed class FakeCorrelationContext : ICorrelationContext
{
    public string CorrelationId { get; private set; } = Guid.NewGuid().ToString("N");

    public void Set(string correlationId) => CorrelationId = correlationId;

    public IDisposable Push(string correlationId)
    {
        CorrelationId = correlationId;
        return new NoopScope();
    }

    private sealed class NoopScope : IDisposable
    {
        public void Dispose() { }
    }
}
