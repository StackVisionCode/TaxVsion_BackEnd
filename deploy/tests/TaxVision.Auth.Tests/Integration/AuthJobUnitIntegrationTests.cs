using System.Net;
using System.Net.Sockets;
using System.Reflection;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.Hosting;
using JasperFx;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Auth.Api.Bootstrap;
using TaxVision.Auth.Api.Jobs;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.TenantDomains.DomainEvents;
using TaxVision.Auth.Application.Users;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.TenantDomains;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Infrastructure.Persistence.Repositories;
using TaxVision.Auth.Tests.Application;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;
using Wolverine.Transports.Tcp;

namespace TaxVision.Auth.Tests.Integration;

/// <summary>
/// AUTH-BB3 contra SQL Server local, en una base temporal propia (se crea con el modelo de Auth y se borra al
/// terminar): los jobs recorren todos los tenants, así que no pueden correr contra la base de desarrollo. Lo
/// que sale hacia otros servicios va a un puerto TCP cerrado y se queda en wolverine_outgoing_envelopes.
/// </summary>
public sealed class AuthJobUnitIntegrationTests : IAsyncLifetime
{
    private readonly string _connectionString;
    private readonly ThrowingFanOutUsers _users = new();
    private readonly FakeCloudflareProvisioningClient _cloudflare = new();
    private IHost _host = null!;

    public AuthJobUnitIntegrationTests()
    {
        var baseConnection =
            Environment.GetEnvironmentVariable("AUTH_DB_CONNECTION")
            ?? new ConfigurationBuilder()
                .AddUserSecrets("c9f518b8-3374-4abd-a7b6-f0b20cb0877f")
                .Build()
                .GetConnectionString("Default")
            ?? throw new InvalidOperationException("Set AUTH_DB_CONNECTION or the Auth user-secrets to run this test.");
        _connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"authbb3_{Guid.NewGuid():N}"[..16],
        }.ConnectionString;
    }

    public async Task InitializeAsync()
    {
        await using (var db = NewContext())
            await db.Database.EnsureCreatedAsync();
        try
        {
            _host = await StartHostAsync();
        }
        catch
        {
            // Si el host no arranca, xUnit no llama DisposeAsync: la base temporal se borra aquí.
            await DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        SqlConnection.ClearAllPools();
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task SystemRolePermissionsSync_FailureAfterSave_DoesNotPersistNorPublish()
    {
        var roleId = await SeedOutdatedSystemRoleAsync();
        _users.Throw = true;

        // Hoy la excepción sale del job; con la unidad transaccional queda registrada y el job sigue.
        try
        {
            await RunSystemRolePermissionsSyncAsync();
        }
        catch (InvalidOperationException) { }

        Assert.Equal(1, await CountRolePermissionsAsync(roleId));
        Assert.Equal(
            0,
            await CountRowsAsync("wolverine_outgoing_envelopes", nameof(RolePermissionsChangedIntegrationEvent))
        );
    }

    [Fact]
    public async Task SystemRolePermissionsSync_CommitsRoleAndNoticeTogether()
    {
        var roleId = await SeedOutdatedSystemRoleAsync();

        await RunSystemRolePermissionsSyncAsync();

        Assert.True(await CountRolePermissionsAsync(roleId) > 1);
        Assert.Equal(
            1,
            await CountRowsAsync("wolverine_outgoing_envelopes", nameof(RolePermissionsChangedIntegrationEvent))
        );
    }

    // El poller guarda fuera de un handler: con la unidad del job, el evento de dominio no se pierde.
    [Fact]
    public async Task ProvisioningPoller_ActivatedDomain_KeepsItsDomainEvent()
    {
        var domain = TenantDomain
            .CreateCustomHostname(Guid.NewGuid(), "portal.example-office.test", Guid.NewGuid(), DateTime.UtcNow)
            .Value;
        domain.MarkProvisioning("cf-1", "txt");
        domain.ClearDomainEvents();
        await using (var db = NewContext())
        {
            db.TenantDomains.Add(domain);
            await db.SaveChangesAsync();
        }

        await RunProvisioningPollAsync();

        Assert.Equal(1, await CountRowsAsync("wolverine_incoming_envelopes", "TenantDomainActivated"));
        Assert.True(
            await WaitUntilAsync(async () =>
                await CountRowsAsync("wolverine_outgoing_envelopes", nameof(TenantDomainActivatedIntegrationEvent)) == 1
            ),
            "El aviso de dominio activado no salió."
        );
        Assert.Equal(
            1,
            await CountRowsAsync("wolverine_outgoing_envelopes", nameof(TenantDomainVerifiedIntegrationEvent))
        );

        // La auditoría del handler de dominio se guarda en su propia transacción, una sola vez.
        await using var verify = NewContext();
        Assert.Equal(1, await verify.AuthAuditLogs.IgnoreQueryFilters().CountAsync(log => log.TargetId == domain.Id));
    }

    private async Task<Guid> SeedOutdatedSystemRoleAsync()
    {
        var tenantId = Guid.NewGuid();
        var role = Role.Create(tenantId, Role.SystemTenantAdmin, null, isSystem: true).Value;
        role.SetPermissions([PermissionCatalog.IdOf(PermissionCatalog.RolesManage)], seeding: true);
        var holder = User.Register(
            tenantId,
            "Role",
            "Holder",
            $"{Guid.NewGuid():N}@example.test",
            "hash",
            UserActorType.TenantAdmin
        ).Value;
        await using var db = NewContext();
        db.Roles.Add(role);
        db.Users.Add(holder);
        db.UserRoles.Add(UserRole.Create(holder.Id, role.Id));
        await db.SaveChangesAsync();
        return role.Id;
    }

    private Task RunSystemRolePermissionsSyncAsync()
    {
        var job = ActivatorUtilities.CreateInstance<SystemRolePermissionsSyncService>(_host.Services);
        var execute = typeof(SystemRolePermissionsSyncService).GetMethod(
            "ExecuteAsync",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        return (Task)execute.Invoke(job, [CancellationToken.None])!;
    }

    private Task RunProvisioningPollAsync()
    {
        var job = ActivatorUtilities.CreateInstance<TenantDomainProvisioningPoller>(_host.Services);
        var poll = typeof(TenantDomainProvisioningPoller).GetMethod(
            "PollOnceAsync",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        return (Task)poll.Invoke(job, [CancellationToken.None])!;
    }

    private async Task<IHost> StartHostAsync()
    {
        var closedPort = FreeTcpPort();
        var connection = _connectionString;
        return await Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                options.ServiceName = "auth-bb3-job-unit-test";
                options.Discovery.DisableConventionalDiscovery().IncludeType(typeof(TenantDomainActivatedHandler));
                options.PersistMessagesWithSqlServer(connection);
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options.Durability.Mode = DurabilityMode.Solo;
                options.Durability.KeepAfterMessageHandling = TimeSpan.FromMinutes(5);

                options.Services.AddDbContext<AuthDbContext>(db => db.UseSqlServer(connection));
                options.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AuthDbContext>());
                options.Services.AddSingleton<ITenantContext>(new NoTenantContext());
                options.Services.AddSingleton<ICorrelationContext>(new FakeCorrelationContext());
                options.Services.AddSingleton<IRequestContext>(new FakeRequestContext());
                options.Services.AddScoped<IAuthAuditWriter, AuthAuditStore>();
                options.Services.AddScoped<IRoleRepository, RoleRepository>();
                options.Services.AddScoped<ITenantDomainRepository, TenantDomainRepository>();
                options.Services.AddScoped<IUserRepository>(sp =>
                {
                    _users.Inner = new UserRepository(sp.GetRequiredService<AuthDbContext>());
                    return _users;
                });
                options.Services.AddSingleton<ICloudflareProvisioningClient>(_cloudflare);
                options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, AuthDbContext>();
                options.Policies.AutoApplyTransactions();

                options.UseTaxVisionDomainEvents();
                options.LocalQueue(DomainEventDispatchExtensions.DomainEventsQueue).Sequential();
                options.UseTaxVisionDurableLocalQueues();

                options.PublishMessage<RolePermissionsChangedIntegrationEvent>().ToPort(closedPort).UseDurableOutbox();
                options.PublishMessage<UserRolesChangedIntegrationEvent>().ToPort(closedPort).UseDurableOutbox();
                options.PublishMessage<TenantDomainVerifiedIntegrationEvent>().ToPort(closedPort).UseDurableOutbox();
                options.PublishMessage<TenantDomainActivatedIntegrationEvent>().ToPort(closedPort).UseDurableOutbox();
            })
            .StartAsync();
    }

    private async Task<int> CountRolePermissionsAsync(Guid roleId)
    {
        await using var db = NewContext();
        return await db
            .Roles.IgnoreQueryFilters()
            .Where(r => r.Id == roleId)
            .SelectMany(r => r.Permissions)
            .CountAsync();
    }

    private async Task<int> CountRowsAsync(string table, string messageType)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"IF OBJECT_ID('[dbo].[{table}]') IS NULL SELECT 0 ELSE SELECT COUNT(*) FROM [dbo].[{table}] WHERE message_type LIKE '%{messageType}%'";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private AuthDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<AuthDbContext>().UseSqlServer(_connectionString).Options,
            new NoTenantContext()
        );

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return true;
            await Task.Delay(200);
        }
        return false;
    }

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    private sealed class StartedLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => new(canceled: true);
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() { }
    }

    /// <summary>Repositorio real que, si se pide, falla en el aviso por titular: después de guardar el rol.</summary>
    private sealed class ThrowingFanOutUsers : DelegatingUserRepository
    {
        public bool Throw { get; set; }

        public override Task<IReadOnlyList<User>> GetActiveByRoleAsync(
            Guid tenantId,
            Guid roleId,
            CancellationToken ct = default
        ) =>
            Throw
                ? throw new InvalidOperationException("Fallo forzado después de guardar.")
                : base.GetActiveByRoleAsync(tenantId, roleId, ct);
    }
}

/// <summary>Delega todo en el repositorio real; las pruebas sobrescriben solo lo que quieren alterar.</summary>
internal abstract class DelegatingUserRepository : IUserRepository
{
    public IUserRepository Inner { get; set; } = null!;

    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) => Inner.GetByIdAsync(id, ct);

    public Task<User?> GetByEmailAsync(
        Guid tenantId,
        string email,
        UserAccountKind kind,
        CancellationToken ct = default
    ) => Inner.GetByEmailAsync(tenantId, email, kind, ct);

    public Task<bool> EmailExistsAsync(
        Guid tenantId,
        string email,
        UserAccountKind kind,
        CancellationToken ct = default
    ) => Inner.EmailExistsAsync(tenantId, email, kind, ct);

    public Task<User?> GetPortalUserByCustomerAsync(Guid tenantId, Guid customerId, CancellationToken ct = default) =>
        Inner.GetPortalUserByCustomerAsync(tenantId, customerId, ct);

    public Task<User?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
        Inner.GetByOnboardingIdAsync(onboardingId, ct);

    public Task<IReadOnlyList<Guid>> GetActiveTenantIdsByEmailAsync(
        string email,
        UserAccountKind kind,
        CancellationToken ct = default
    ) => Inner.GetActiveTenantIdsByEmailAsync(email, kind, ct);

    public Task AddAsync(User user, CancellationToken ct = default) => Inner.AddAsync(user, ct);

    public Task<int> CountActiveAsync(Guid tenantId, CancellationToken ct = default) =>
        Inner.CountActiveAsync(tenantId, ct);

    public Task<IReadOnlyList<User>> GetActiveStaffForSlotGatingAsync(Guid tenantId, CancellationToken ct = default) =>
        Inner.GetActiveStaffForSlotGatingAsync(tenantId, ct);

    public Task<User?> GetPrimaryAdminAsync(Guid tenantId, CancellationToken ct = default) =>
        Inner.GetPrimaryAdminAsync(tenantId, ct);

    public Task<int> CountActiveAdminsAsync(Guid tenantId, CancellationToken ct = default) =>
        Inner.CountActiveAdminsAsync(tenantId, ct);

    public virtual Task<IReadOnlyList<User>> GetActiveByRoleAsync(
        Guid tenantId,
        Guid roleId,
        CancellationToken ct = default
    ) => Inner.GetActiveByRoleAsync(tenantId, roleId, ct);

    public Task<(IReadOnlyList<User> Items, int TotalCount)> GetPagedAsync(
        Guid tenantId,
        int page,
        int size,
        string? search,
        bool? isActive,
        Guid? customerId = null,
        UserAccountKind? accountKind = null,
        CancellationToken ct = default
    ) => Inner.GetPagedAsync(tenantId, page, size, search, isActive, customerId, accountKind, ct);

    public Task<PagedResult<TenantOwnerReconciliationResponse>> ListTenantOwnersForReconciliationAsync(
        int page,
        int size,
        CancellationToken ct = default
    ) => Inner.ListTenantOwnersForReconciliationAsync(page, size, ct);

    public Task<PagedResult<CustomerPortalAccountReconciliationResponse>> ListPortalAccountsForReconciliationAsync(
        int page,
        int size,
        CancellationToken ct = default
    ) => Inner.ListPortalAccountsForReconciliationAsync(page, size, ct);

    public Task<PagedResult<StaffUserReconciliationResponse>> ListStaffUsersForReconciliationAsync(
        int page,
        int size,
        CancellationToken ct = default
    ) => Inner.ListStaffUsersForReconciliationAsync(page, size, ct);

    public Task<PagedResult<UserDirectoryReconciliationResponse>> ListUsersForDirectoryReconciliationAsync(
        int page,
        int size,
        CancellationToken ct = default
    ) => Inner.ListUsersForDirectoryReconciliationAsync(page, size, ct);
}
