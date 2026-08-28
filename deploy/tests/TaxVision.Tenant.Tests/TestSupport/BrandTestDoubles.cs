using System.Collections.Generic;
using System.Linq;
using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tenant.Application.Brands.Abstractions;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using TaxVision.Tenant.Domain.Brands;
using TaxVision.Tenant.Domain.Enums;
using Wolverine;

namespace TaxVision.Tenant.Tests.TestSupport;

/// <summary>Cliente de CloudStorage de mentira: el delete siempre pasa; upload/download no se usan.</summary>
internal sealed class FakeBrandingCloudStorageClient : ITenantBrandingCloudStorageClient
{
    public List<Guid> Deleted { get; } = [];

    public Task<Result<TenantBrandStoredFile>> StoreAsync(
        Guid tenantId,
        TenantLogoUpload upload,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task RequestCatalogAsync(
        Guid tenantId,
        TenantLogoUpload upload,
        TenantBrandStoredFile stored,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<Result<TenantLogoDownloadUrl>> GetDownloadUrlAsync(
        Guid tenantId,
        Guid fileId,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<Result> DeleteAsync(Guid tenantId, Guid fileId, CancellationToken ct = default)
    {
        Deleted.Add(fileId);
        return Task.FromResult(Result.Success());
    }
}

/// <summary>Fakes compartidos por los tests de marca (consumer + comandos con eventos).</summary>
internal sealed class RecordingMessageBus : IMessageBus
{
    public List<object> Published { get; } = [];

    public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null)
    {
        if (message is not null)
            Published.Add(message);
        return ValueTask.CompletedTask;
    }

    public ValueTask SendAsync<T>(T message, DeliveryOptions? options = null) => throw new NotImplementedException();

    public ValueTask BroadcastToTopicAsync(string topicName, object message, DeliveryOptions? options = null) =>
        throw new NotImplementedException();

    public IReadOnlyList<Envelope> PreviewSubscriptions(object message) => throw new NotImplementedException();

    public IReadOnlyList<Envelope> PreviewSubscriptions(object message, DeliveryOptions options) =>
        throw new NotImplementedException();

    public IDestinationEndpoint EndpointFor(string endpointName) => throw new NotImplementedException();

    public IDestinationEndpoint EndpointFor(Uri uri) => throw new NotImplementedException();

    public Task InvokeForTenantAsync(
        string tenantId,
        object message,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    ) => throw new NotImplementedException();

    public Task<T> InvokeForTenantAsync<T>(
        string tenantId,
        object message,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    ) => throw new NotImplementedException();

    public string? TenantId
    {
        get => null;
        set { }
    }

    public Task InvokeAsync(object message, CancellationToken cancellation = default, TimeSpan? timeout = null) =>
        throw new NotImplementedException();

    public Task InvokeAsync(
        object message,
        DeliveryOptions options,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    ) => throw new NotImplementedException();

    public Task<T> InvokeAsync<T>(object message, CancellationToken cancellation = default, TimeSpan? timeout = null) =>
        throw new NotImplementedException();

    public Task<T> InvokeAsync<T>(
        object message,
        DeliveryOptions options,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    ) => throw new NotImplementedException();

    public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
        object message,
        CancellationToken cancellation = default
    ) => throw new NotImplementedException();

    public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
        object message,
        DeliveryOptions options,
        CancellationToken cancellation = default
    ) => throw new NotImplementedException();
}

internal sealed class NoopCache : BuildingBlocks.Caching.ICacheService
{
    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) => Task.FromResult<T?>(default);

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;

    public Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? ttl = null,
        CancellationToken ct = default
    ) => factory(ct);
}

internal sealed class NoopCorrelationContext : ICorrelationContext
{
    public string CorrelationId => "test-correlation-id";

    public void Set(string correlationId) { }

    public IDisposable Push(string correlationId) => new NoopScope();

    private sealed class NoopScope : IDisposable
    {
        public void Dispose() { }
    }
}

internal sealed class CountingUnitOfWork : IUnitOfWork
{
    public int SaveChangesCallCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        SaveChangesCallCount++;
        return Task.FromResult(1);
    }
}

/// <summary>Repo de marca en memoria que soporta la correlación por fileId del consumer de escaneo.</summary>
internal sealed class InMemoryBrandRepository : ITenantBrandRepository
{
    public List<TenantBrand> All { get; } = [];

    public TenantBrand Seed(Guid tenantId, BrandSurface surface, Action<TenantBrand> configure)
    {
        var brand = TenantBrand.Create(tenantId, surface);
        configure(brand);
        All.Add(brand);
        return brand;
    }

    public Task<TenantBrand?> GetAsync(Guid tenantId, BrandSurface surface, CancellationToken ct = default) =>
        Task.FromResult(All.FirstOrDefault(b => b.TenantId == tenantId && b.Surface == surface));

    public Task<IReadOnlyList<TenantBrand>> ListAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TenantBrand>>(All.Where(b => b.TenantId == tenantId).ToList());

    public Task AddAsync(TenantBrand brand, CancellationToken ct = default)
    {
        All.Add(brand);
        return Task.CompletedTask;
    }

    public Task<TenantBrandAsset?> GetConfirmedAssetByFileIdAsync(Guid fileId, CancellationToken ct = default) =>
        Task.FromResult(
            All.SelectMany(b => b.Assets)
                .FirstOrDefault(a => a.FileId == fileId && a.Status == BrandAssetStatus.Confirmed)
        );

    public Task<TenantBrand?> GetByAssetFileIdAsync(Guid tenantId, Guid fileId, CancellationToken ct = default) =>
        Task.FromResult(All.FirstOrDefault(b => b.TenantId == tenantId && b.Assets.Any(a => a.FileId == fileId)));
}
