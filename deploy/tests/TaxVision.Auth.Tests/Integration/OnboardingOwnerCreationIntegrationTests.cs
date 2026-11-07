using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using BuildingBlocks.Common;
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
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Infrastructure.Persistence.Repositories;
using TaxVision.Auth.Onboarding.Application.Abstractions;
using TaxVision.Auth.Onboarding.Application.Internal.Commands;
using TaxVision.Auth.Onboarding.Application.Sagas.Commands;
using TaxVision.Auth.Onboarding.Domain.TenantOnboardings;
using TaxVision.Auth.Onboarding.Infrastructure.Persistence.Repositories;
using TaxVision.Auth.Tests.Application;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;
using Wolverine.Transports.Tcp;

namespace TaxVision.Auth.Tests.Integration;

/// <summary>
/// AUTH-8, con Wolverine y SQL de verdad: el paso de la Saga invoca al handler real en proceso, que abre su propia
/// transacción y su outbox. El aviso que hace avanzar la Saga se cuenta al llegar a un puerto.
/// </summary>
public sealed class OnboardingOwnerCreationIntegrationTests : IAsyncLifetime
{
    private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(3);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _onboardingId = Guid.NewGuid();
    private readonly OneShotPasswords _passwords = new();
    private readonly FailOnUserInsert _insertFailure = new();
    private AuthTestDatabase _database = null!;
    private IHost _host = null!;

    public async Task InitializeAsync()
    {
        _database = await AuthTestDatabase.CreateAsync();
        _host = await StartHostAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
        await _database.DisposeAsync();
    }

    [Fact(Timeout = 120_000)]
    public async Task Step_CreatesTheOwner_AndARetryChangesNothing()
    {
        var first = await RunStepAsync();
        var second = await RunStepAsync();
        await Task.Delay(SettleTime);

        Assert.Null(first);
        Assert.Null(second);
        await using var db = _database.NewContext(_tenantId);
        var owner = await db.Users.IgnoreQueryFilters().SingleAsync(user => user.OnboardingId == _onboardingId);
        Assert.Equal(UserActorType.TenantAdmin, owner.ActorType);
        Assert.Equal("hash-from-reference", owner.PasswordHash);
        Assert.Equal(owner.Id, Assert.Single(OwnerCreatedProbeHandler.For(_onboardingId)));
    }

    [Fact(Timeout = 120_000)]
    public async Task ExpiredPasswordReference_FailsTheStep_WithoutAnOwner()
    {
        _passwords.Consume();

        var failed = await RunStepAsync();

        Assert.Equal(TenantProvisioningStep.TenantAdmin.ToString(), failed!.FailedStep);
        Assert.Equal("Onboarding.PasswordReferenceExpired", failed.FailureCode);
        await AssertNoOwnerAsync();
    }

    [Fact(Timeout = 120_000)]
    public async Task SaveThrows_FailsTheStep_LeavesNoOwnerAndNoNotice()
    {
        _insertFailure.Armed = true;

        var failed = await RunStepAsync();
        await Task.Delay(SettleTime);

        Assert.Equal("Onboarding.OwnerCreationFailed", failed!.FailureCode);
        await AssertNoOwnerAsync();
        Assert.Empty(OwnerCreatedProbeHandler.For(_onboardingId));
    }

    private async Task AssertNoOwnerAsync()
    {
        await using var db = _database.NewContext(_tenantId);
        Assert.False(await db.Users.IgnoreQueryFilters().AnyAsync(user => user.OnboardingId == _onboardingId));
    }

    private Task<OnboardingProvisioningStepFailedIntegrationEvent?> RunStepAsync() =>
        _host
            .Services.GetRequiredService<IMessageBus>()
            .InvokeAsync<OnboardingProvisioningStepFailedIntegrationEvent?>(
                new CreateTenantOwnerCommand(
                    _onboardingId,
                    _tenantId,
                    $"{Guid.NewGuid():N}@example.test",
                    "Ada",
                    "Lovelace",
                    OneShotPasswords.Reference
                )
            );

    private Task<IHost> StartHostAsync() =>
        Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging.ClearProviders())
            .UseWolverine(options =>
            {
                var port = FreeTcpPort();
                options.ServiceName = "auth-owner-creation-test";
                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options
                    .Discovery.DisableConventionalDiscovery()
                    .IncludeType(typeof(CreateTenantOwnerHandler))
                    .IncludeType(typeof(CreateTenantOwnerFromOnboardingHandler))
                    .IncludeType(typeof(OwnerCreatedProbeHandler));
                options.PersistMessagesWithSqlServer(_database.ConnectionString, "wolverine_test");
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options.Durability.Mode = DurabilityMode.Solo;
                options.Durability.DrainTimeout = TimeSpan.FromSeconds(2);

                var services = options.Services;
                services.AddDbContext<AuthDbContext>(db =>
                    db.UseSqlServer(_database.ConnectionString).AddInterceptors(_insertFailure)
                );
                services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AuthDbContext>());
                services.AddSingleton<ITenantContext>(new NoTenant());
                services.AddScoped<IUserRepository, UserRepository>();
                services.AddScoped<IRoleRepository, RoleRepository>();
                services.AddScoped<ITenantOnboardingRepository, TenantOnboardingRepository>();
                services.AddScoped<ITenantTermsAcceptanceRepository, TenantTermsAcceptanceRepository>();
                services.AddScoped<ITermsVersionRepository, TermsVersionRepository>();
                services.AddScoped<IAuthAuditWriter, AuthAuditStore>();
                services.AddSingleton<ITokenReferenceStore>(_passwords);
                services.AddSingleton<ICorrelationContext>(new FakeCorrelationContext());
                options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, AuthDbContext>();
                options.Policies.AutoApplyTransactions();

                options.ListenAtPort(port).UseDurableInbox();
                options.PublishMessage<TenantOwnerCreatedIntegrationEvent>().ToPort(port).UseDurableOutbox();
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

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }

    /// <summary>La referencia del hash es de un solo uso, como el GETDEL de Redis.</summary>
    private sealed class OneShotPasswords : ITokenReferenceStore
    {
        public static readonly Guid Reference = Guid.NewGuid();
        private int _consumed;

        public void Consume() => Interlocked.Exchange(ref _consumed, 1);

        public Task<string?> ConsumeAsync(Guid reference, CancellationToken ct = default) =>
            Task.FromResult(Interlocked.Exchange(ref _consumed, 1) == 0 ? "hash-from-reference" : null);

        public Task<Guid> StoreAsync(string rawToken, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task StoreAsync(Guid reference, string rawToken, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task StoreAsync(Guid reference, string rawToken, TimeSpan ttl, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<string?> PeekAsync(Guid reference, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FailOnUserInsert : SaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<User>().Any(e => e.State == EntityState.Added))
                throw new TimeoutException("Simulated SQL timeout.");
            return ValueTask.FromResult(result);
        }
    }
}

/// <summary>Lo que de verdad cruzó el puerto, por onboarding.</summary>
public static class OwnerCreatedProbeHandler
{
    private static readonly ConcurrentQueue<(Guid OnboardingId, Guid UserId)> Received = new();

    public static IReadOnlyList<Guid> For(Guid onboardingId) =>
        Received.Where(entry => entry.OnboardingId == onboardingId).Select(entry => entry.UserId).ToArray();

    public static void Handle(TenantOwnerCreatedIntegrationEvent message) =>
        Received.Enqueue((message.OnboardingId, message.CreatedUserId));
}
