using BuildingBlocks.Caching;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.Tenant.Application.Brands.Abstractions;
using TaxVision.Tenant.Application.Brands.Commands;
using TaxVision.Tenant.Domain.Enums;
using TaxVision.Tenant.Infrastructure.Persistence.Repositories;
using Wolverine;

namespace TaxVision.Tenant.Tests.Integration;

/// <summary>
/// TEN-6 con el pipeline real y SQL Server, en base temporal: cuando se borra la caché, el cambio ya tiene que
/// verse desde otra conexión. Si no, una lectura en ese hueco vuelve a cachear lo viejo.
/// </summary>
public sealed class TenantBrandCacheInvalidationIntegrationTests : IAsyncLifetime
{
    private readonly TenantTemporaryDatabase _database = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private CommitProbeCache? _cache;
    private IHost? _host;

    public async Task InitializeAsync()
    {
        await _database.CreateAsync();
        _cache = new CommitProbeCache(_database.ConnectionString, _tenantId);
        var cache = _cache;
        _host = await TenantWolverineHost.StartAsync(
            _database.ConnectionString,
            (options, _) =>
            {
                options.Discovery.IncludeType(typeof(UpdateTenantBrandColorsHandler));
                options.Discovery.IncludeType(typeof(InvalidateTenantBrandCacheHandler));
                options.Services.AddScoped<ITenantBrandRepository, TenantBrandRepository>();
                options.Services.AddSingleton<ICacheService>(cache);
                options.UseTaxVisionDurableLocalQueues();
            }
        );
    }

    public async Task DisposeAsync()
    {
        await TenantWolverineHost.StopAsync(_host);
        await _database.DropAsync();
    }

    [Fact(Timeout = 60_000)]
    public async Task UpdateColors_InvalidatesTheCacheAfterCommit()
    {
        var result = await _host!
            .Services.GetRequiredService<IMessageBus>()
            .InvokeAsync<Result>(new UpdateTenantBrandColorsCommand(_tenantId, BrandSurface.Crm, "#112233", null));

        Assert.True(result.IsSuccess);
        var committedWhenInvalidated = await _cache!.Invalidated.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(committedWhenInvalidated);
    }

    /// <summary>Al borrar, mira desde otra conexión si la marca ya está confirmada; READPAST salta lo no confirmado.</summary>
    private sealed class CommitProbeCache(string connectionString, Guid tenantId) : ICacheService
    {
        public TaskCompletionSource<bool> Invalidated { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task RemoveAsync(string key, CancellationToken ct = default)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM TenantBrands WITH (READPAST) WHERE TenantId = @tenantId";
            command.Parameters.AddWithValue("@tenantId", tenantId);
            Invalidated.TrySetResult(Convert.ToInt32(await command.ExecuteScalarAsync(ct)) == 1);
        }

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) => Task.FromResult(default(T));

        public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<T> GetOrCreateAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> factory,
            TimeSpan? ttl = null,
            CancellationToken ct = default
        ) => factory(ct);
    }
}
