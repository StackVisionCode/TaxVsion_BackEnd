using System.Net;
using System.Net.Sockets;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using JasperFx;
using JasperFx.CodeGeneration.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Infrastructure.Persistence;
using TaxVision.Customer.Infrastructure.Persistence.Repositories;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;

namespace TaxVision.Customer.Tests.Integration;

/// <summary>
/// Host mínimo de Wolverine sobre la base temporal, con las piezas de Program.cs que importan a estas pruebas:
/// transacción EF por handler, política de fallos estándar y sobres en la misma base. Sin Rabbit.
/// </summary>
internal static class CustomerWolverineHost
{
    // Cortos para la prueba: 3 esperas dan 4 intentos antes de la cola de errores.
    private static readonly TimeSpan[] ConflictRetryDelays =
    [
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(100),
    ];

    public static Task<IHost> StartAsync(
        CustomerTemporaryDatabase database,
        Guid tenantId,
        Action<WolverineOptions> configure,
        params IInterceptor[] interceptors
    ) =>
        Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                options.ServiceName = "customer-int-test";
                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options.Discovery.DisableConventionalDiscovery();
                options.PersistMessagesWithSqlServer(database.ConnectionString, "wolverine");
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options.Durability.Mode = DurabilityMode.Solo;
                options.Durability.DrainTimeout = TimeSpan.FromSeconds(2);

                options.Services.AddDbContext<CustomerDbContext>(db =>
                    db.UseSqlServer(database.ConnectionString).AddInterceptors(interceptors)
                );
                options.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CustomerDbContext>());
                options.Services.AddSingleton<ITenantContext>(new CustomerTemporaryDatabase.FixedTenant(tenantId));
                options.Services.AddSingleton<ICorrelationContext, NoCorrelation>();
                options.Services.AddScoped<ICustomerRepository, CustomerRepository>();
                options.Services.AddScoped<ITenantEmployeeDirectoryRepository, TenantEmployeeDirectoryRepository>();
                options.Services.AddScoped<ICustomerCreationLock, SqlCustomerCreationLock>();

                options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, CustomerDbContext>();
                options.Policies.AutoApplyTransactions();
                options.UseTaxVisionConflictPolicy(new ConflictRetryOptions { Delays = ConflictRetryDelays });
                options.ApplyStandardFailurePolicies();

                configure(options);
            })
            .StartAsync();

    /// <summary>Puerto cerrado: lo publicado se queda en <c>wolverine_outgoing_envelopes</c> y se puede contar.</summary>
    public static int ClosedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class NoCorrelation : ICorrelationContext
    {
        public string CorrelationId => "customer-int-test";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new Scope();

        private sealed class Scope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
