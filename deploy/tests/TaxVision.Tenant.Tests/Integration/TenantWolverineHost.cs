using System.Net;
using System.Net.Sockets;
using BuildingBlocks.Caching;
using BuildingBlocks.Persistence;
using BuildingBlocks.Web.Common;
using JasperFx;
using JasperFx.CodeGeneration.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.Tenant.Application.Compliance.Abstractions;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using TaxVision.Tenant.Infrastructure.Persistence;
using TaxVision.Tenant.Infrastructure.Persistence.Repositories;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;

namespace TaxVision.Tenant.Tests.Integration;

/// <summary>
/// Host mínimo con las mismas piezas de Program.cs (transacciones EF, AutoApplyTransactions, AddDbContext
/// normal), sin Rabbit: lo que sale hacia otros servicios va a un puerto TCP cerrado y se queda en
/// wolverine_outgoing_envelopes de la base temporal.
/// </summary>
internal static class TenantWolverineHost
{
    public const string EnvelopeSchema = "wolverine_test";

    public static Task<IHost> StartAsync(
        string connectionString,
        Action<WolverineOptions, int> configure,
        params IInterceptor[] interceptors
    ) =>
        Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                options.ServiceName = "tenant-int-test";
                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options.Discovery.DisableConventionalDiscovery();
                options.PersistMessagesWithSqlServer(connectionString, EnvelopeSchema);
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options.Durability.Mode = DurabilityMode.Solo;
                options.Durability.KeepAfterMessageHandling = TimeSpan.FromMinutes(5);
                options.Durability.DrainTimeout = TimeSpan.FromSeconds(2);

                options.Services.AddBuildingBlocks();
                options.Services.AddDbContext<TenantDbContext>(db =>
                    db.UseSqlServer(connectionString).AddInterceptors(interceptors)
                );
                options.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<TenantDbContext>());
                options.Services.AddScoped<ITenantRepository, TenantRepository>();
                options.Services.AddScoped<IOfficeComplianceRepository, OfficeComplianceRepository>();
                options.Services.AddSingleton<ICacheService, NoOpCacheService>();
                options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, TenantDbContext>();
                options.Policies.AutoApplyTransactions();

                configure(options, FreeTcpPort());
            })
            .StartAsync();

    public static async Task StopAsync(IHost? host)
    {
        if (host is null)
            return;
        await host.StopAsync();
        host.Dispose();
    }

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class NoOpCacheService : ICacheService
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) => Task.FromResult(default(T));

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
}

/// <summary>Falla justo después de guardar, antes de que la unidad confirme.</summary>
internal sealed class FailAfterSaveInterceptor : SaveChangesInterceptor
{
    public bool Armed { get; set; }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default
    )
    {
        if (!Armed)
            return ValueTask.FromResult(result);
        Armed = false;
        throw new InvalidOperationException("Fallo forzado después de guardar.");
    }
}
