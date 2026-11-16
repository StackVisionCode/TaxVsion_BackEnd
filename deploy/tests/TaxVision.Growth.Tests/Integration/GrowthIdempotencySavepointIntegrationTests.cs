using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Codes.Domain.ValueObjects;
using TaxVision.Growth.Infrastructure;
using TaxVision.Growth.Infrastructure.Idempotency;
using TaxVision.Growth.Infrastructure.Persistence;

namespace TaxVision.Growth.Tests.Integration;

/// <summary>
/// Contra SQL Server real, en una base temporal, con la cadena como la de producción (con MARS): dentro de la
/// transacción que abre el handler, el ejecutor de idempotencia tiene que poder crear su savepoint.
/// </summary>
public sealed class GrowthIdempotencySavepointIntegrationTests : IAsyncLifetime
{
    private readonly GrowthTemporaryDatabase _database = new();

    public Task InitializeAsync() => _database.CreateAsync();

    public Task DisposeAsync() => _database.DropAsync();

    [Fact]
    public async Task The_idempotency_executor_runs_inside_a_handler_transaction_with_a_mars_connection_string()
    {
        var tenantId = Guid.NewGuid();
        var withMars = new SqlConnectionStringBuilder(_database.ConnectionString)
        {
            MultipleActiveResultSets = true,
        }.ConnectionString;
        await using var provider = Services(withMars, tenantId);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GrowthDbContext>();
        await using var handlerTransaction = await db.Database.BeginTransactionAsync();

        var result = await scope
            .ServiceProvider.GetRequiredService<SqlBusinessIdempotencyExecutor>()
            .ExecuteAsync(
                tenantId,
                "Referrals.SavepointProbe.v1",
                Guid.NewGuid(),
                IdempotencyKey.Create($"probe-{Guid.NewGuid():N}").Value,
                PayloadFingerprint.Create(new string('a', 64)).Value,
                _ => Task.FromResult(Result.Success(42))
            );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(42, result.Value);
    }

    private static ServiceProvider Services(string connectionString, Guid tenantId)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connectionString })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGrowthInfrastructure(configuration);
        services.AddScoped<ITenantContext>(_ => new FixedTenant(tenantId));
        services.AddSingleton(TimeProvider.System);
        return services.BuildServiceProvider();
    }

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }
}
