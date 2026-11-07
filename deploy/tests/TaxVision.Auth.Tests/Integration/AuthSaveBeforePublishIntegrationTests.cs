using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Sockets;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using JasperFx;
using JasperFx.CodeGeneration.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Credentials.Commands;
using TaxVision.Auth.Domain.Credentials;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Infrastructure.Persistence.Repositories;
using TaxVision.Auth.Infrastructure.Security;
using TaxVision.Auth.Tests.Application;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;
using Wolverine.Transports.Tcp;

namespace TaxVision.Auth.Tests.Integration;

/// <summary>
/// BB-16. Por <c>InvokeAsync</c>, como entra por HTTP, y contado al llegar a un puerto en una base temporal: del intento
/// que falla al guardar no sale nada, y el enlace que llega es el que quedó guardado. Publicar después de guardar sigue
/// siendo atómico: la transacción la abre Wolverine antes del handler y se confirma después.
///
/// <para><b>Límite medido:</b> si lo que falla es el commit y hay reintento en línea, salen 2 avisos aunque se publique
/// después de guardar. Ese reintento conserva lo publicado en el intento anterior; el orden no lo arregla.</para>
/// </summary>
public sealed class AuthSaveBeforePublishIntegrationTests : IAsyncLifetime
{
    private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(3);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly FailOnceOnSave _saveFailure = new();
    private readonly FailOnceOnCommit _commitFailure = new();
    private AuthTestDatabase _database = null!;
    private IHost _host = null!;

    public async Task InitializeAsync()
    {
        _database = await AuthTestDatabase.CreateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        await _database.DisposeAsync();
    }

    [Fact(Timeout = 120_000)]
    public async Task SaveFails_OnlyTheLinkThatWasSavedGoesOut()
    {
        _host = await StartHostAsync(retries: true);
        var user = await SeedUserAsync();
        _saveFailure.Armed = true;

        var result = await RequestEmailChangeAsync(user.Id);
        await Task.Delay(SettleTime);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, _saveFailure.Failures);
        var rawToken = Assert.Single(EmailChangeProbeHandler.TokensFor(user.Id));
        await using var db = _database.NewContext(_tenantId);
        var stored = await db.EmailVerificationTokens.IgnoreQueryFilters().SingleAsync(t => t.UserId == user.Id);
        Assert.Equal(new SecureTokenService().Hash(rawToken), stored.TokenHash);
    }

    [Fact(Timeout = 120_000)]
    public async Task CommitFails_LeavesNeitherTheRowNorTheMessage()
    {
        _host = await StartHostAsync(retries: false);
        var user = await SeedUserAsync();
        _commitFailure.Armed = true;

        await Assert.ThrowsAsync<TimeoutException>(() => RequestEmailChangeAsync(user.Id));
        await Task.Delay(SettleTime);

        Assert.Equal(1, _commitFailure.Failures);
        Assert.Empty(EmailChangeProbeHandler.TokensFor(user.Id));
        await using var db = _database.NewContext(_tenantId);
        Assert.False(await db.EmailVerificationTokens.IgnoreQueryFilters().AnyAsync(t => t.UserId == user.Id));
    }

    private Task<BuildingBlocks.Results.Result> RequestEmailChangeAsync(Guid userId) =>
        _host
            .Services.GetRequiredService<IMessageBus>()
            .InvokeAsync<BuildingBlocks.Results.Result>(
                new RequestEmailChangeCommand(userId, $"{Guid.NewGuid():N}@example.test")
            );

    private async Task<User> SeedUserAsync()
    {
        var user = User.Register(
            _tenantId,
            "Erin",
            "Preparer",
            $"{Guid.NewGuid():N}@example.test",
            "hash",
            UserActorType.TenantEmployee
        ).Value;
        await using var db = _database.NewContext(_tenantId);
        db.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private Task<IHost> StartHostAsync(bool retries) =>
        Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging.ClearProviders())
            .UseWolverine(options =>
            {
                var port = FreeTcpPort();
                options.ServiceName = "auth-bb16-test";
                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options
                    .Discovery.DisableConventionalDiscovery()
                    .IncludeType(typeof(RequestEmailChangeHandler))
                    .IncludeType(typeof(EmailChangeProbeHandler));
                options.PersistMessagesWithSqlServer(_database.ConnectionString, "wolverine_test");
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options.Durability.Mode = DurabilityMode.Solo;
                options.Durability.DrainTimeout = TimeSpan.FromSeconds(2);

                var services = options.Services;
                services.AddDbContext<AuthDbContext>(db =>
                    db.UseSqlServer(_database.ConnectionString).AddInterceptors(_saveFailure, _commitFailure)
                );
                services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AuthDbContext>());
                services.AddSingleton<ITenantContext>(new FixedTenant(_tenantId));
                services.AddScoped<IUserRepository, UserRepository>();
                services.AddScoped<ITenantRegistry, TenantRegistry>();
                services.AddScoped<ICredentialTokenRepository, CredentialTokenRepository>();
                services.AddScoped<IAuthAuditWriter, AuthAuditStore>();
                services.AddSingleton<ISecureTokenService, SecureTokenService>();
                services.AddSingleton<IAccountTokenProtector>(new AccountTokenProtector(null));
                services.AddSingleton<IRequestContext>(new FakeRequestContext());
                services.AddSingleton<ICorrelationContext>(new FakeCorrelationContext());
                options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, AuthDbContext>();
                options.Policies.AutoApplyTransactions();
                if (retries)
                    options.ApplyStandardFailurePolicies();

                options.ListenAtPort(port).UseDurableInbox();
                options.PublishMessage<EmailChangeRequestedIntegrationEvent>().ToPort(port).UseDurableOutbox();
            })
            .StartAsync();

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }

    /// <summary>Falla una vez el guardado que lleva el token, como un timeout de SQL.</summary>
    private sealed class FailOnceOnSave : SaveChangesInterceptor
    {
        private int _failures;

        public bool Armed { get; set; }
        public int Failures => Volatile.Read(ref _failures);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            var saving = eventData
                .Context!.ChangeTracker.Entries<EmailVerificationToken>()
                .Any(entry => entry.State == EntityState.Added);
            if (saving && Armed)
            {
                Armed = false;
                Interlocked.Increment(ref _failures);
                throw new TimeoutException("Simulated SQL timeout.");
            }
            return ValueTask.FromResult(result);
        }
    }

    /// <summary>Falla una vez la confirmación: el handler ya guardó y ya publicó.</summary>
    private sealed class FailOnceOnCommit : DbTransactionInterceptor
    {
        private int _failures;

        public bool Armed { get; set; }
        public int Failures => Volatile.Read(ref _failures);

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            if (Armed)
            {
                Armed = false;
                Interlocked.Increment(ref _failures);
                throw new TimeoutException("Simulated commit failure.");
            }
            return ValueTask.FromResult(result);
        }
    }
}

/// <summary>Lo que de verdad cruzó el puerto, por usuario.</summary>
public static class EmailChangeProbeHandler
{
    private static readonly ConcurrentQueue<(Guid UserId, string RawToken)> Received = new();

    public static IReadOnlyList<string> TokensFor(Guid userId) =>
        Received.Where(entry => entry.UserId == userId).Select(entry => entry.RawToken).ToArray();

    public static void Handle(EmailChangeRequestedIntegrationEvent message) =>
        Received.Enqueue((message.UserId, message.RawToken));
}
