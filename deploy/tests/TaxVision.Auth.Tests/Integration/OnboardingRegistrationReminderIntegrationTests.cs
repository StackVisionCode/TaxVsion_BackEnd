using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using JasperFx;
using JasperFx.CodeGeneration.Model;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Api.Jobs;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Onboarding;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Application.Onboarding.TenantOnboardings.Services;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;
using TaxVision.Auth.Domain.Onboarding.ValueObjects;
using TaxVision.Auth.Infrastructure.Onboarding.Persistence.Repositories;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Tests.Onboarding;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;
using Wolverine.Transports.Tcp;

namespace TaxVision.Auth.Tests.Integration;

/// <summary>
/// BB-14. El correo "completa tu oficina" se cuenta al llegar a un puerto, en una base temporal propia. Si el marcado
/// no se confirma, no sale nada, y el barrido siguiente lo manda una sola vez.
/// </summary>
public sealed class OnboardingRegistrationReminderIntegrationTests : IAsyncLifetime
{
    private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(3);

    private readonly FailOnceInterceptor _failure = new();
    private readonly FakeTokenReferenceStore _tokens = new() { ToPeek = "raw-token" };
    private readonly string _connectionString;
    private IHost _host = null!;

    public OnboardingRegistrationReminderIntegrationTests()
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
            InitialCatalog = $"authbb14_{Guid.NewGuid():N}"[..17],
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

    [Fact(Timeout = 120_000)]
    public async Task Sweep_WhenTheMarkIsNotSaved_SendsNothingAndTheNextSweepSendsOnce()
    {
        var onboardingId = await SeedDueOnboardingAsync();
        _failure.Armed = true;

        Assert.Equal(0, await SweepAsync());
        await Task.Delay(SettleTime);

        Assert.Equal(1, _failure.Failures);
        Assert.Equal(0, RegistrationReadyProbeHandler.CountFor(onboardingId));
        Assert.Null(await EmailSentAtAsync(onboardingId));

        Assert.Equal(1, await SweepAsync());
        await Task.Delay(SettleTime);

        Assert.Equal(1, RegistrationReadyProbeHandler.CountFor(onboardingId));
        Assert.NotNull(await EmailSentAtAsync(onboardingId));
    }

    private Task<int> SweepAsync() =>
        ActivatorUtilities
            .CreateInstance<OnboardingRegistrationReminderScheduler>(_host.Services)
            .RunOnceAsync(DateTime.UtcNow, CancellationToken.None);

    private async Task<Guid> SeedDueOnboardingAsync()
    {
        var now = DateTime.UtcNow;
        var onboarding = TenantOnboarding
            .Create($"{Guid.NewGuid():N}@example.test", now, Guid.NewGuid(), "Ada", "Lovelace", null, now)
            .Value;
        onboarding.MarkPaymentProcessing(Guid.NewGuid(), "cs_test");
        onboarding.MarkPaymentCompleted("cs_test", now.AddMinutes(-60));
        onboarding.RecordSettledAmount(4900, "USD");
        onboarding.SetRegistrationToken(
            RegistrationTokenHash.Create(new string('a', 64)).Value,
            now.AddHours(72),
            _tokens.Reference
        );
        await using var db = NewContext();
        db.TenantOnboardings.Add(onboarding);
        await db.SaveChangesAsync();
        return onboarding.Id;
    }

    private async Task<DateTime?> EmailSentAtAsync(Guid onboardingId)
    {
        await using var db = NewContext();
        return await db
            .TenantOnboardings.Where(onboarding => onboarding.Id == onboardingId)
            .Select(onboarding => onboarding.RegistrationEmailSentAtUtc)
            .SingleAsync();
    }

    private Task<IHost> StartHostAsync() =>
        Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging.ClearProviders())
            .UseWolverine(options =>
            {
                var port = FreeTcpPort();
                options.ServiceName = "auth-bb14-reminder-test";
                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options.Discovery.DisableConventionalDiscovery().IncludeType(typeof(RegistrationReadyProbeHandler));
                options.PersistMessagesWithSqlServer(_connectionString, "wolverine_test");
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options.Durability.Mode = DurabilityMode.Solo;
                options.Durability.DrainTimeout = TimeSpan.FromSeconds(2);

                var services = options.Services;
                services.AddDbContext<AuthDbContext>(db =>
                    db.UseSqlServer(_connectionString).AddInterceptors(_failure)
                );
                services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AuthDbContext>());
                services.AddSingleton<ITenantContext>(new NoTenantContext());
                services.AddScoped<ITenantOnboardingRepository, TenantOnboardingRepository>();
                services.AddSingleton<ITokenReferenceStore>(_tokens);
                services.AddSingleton<IPlanCatalogClient>(new FakePlanCatalogClient("Pro"));
                services.AddSingleton(Options.Create(new OnboardingOptions { RegistrationReminderDelayMinutes = 45 }));
                services.AddScoped<OnboardingRegistrationReminderProcessor>();
                options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, AuthDbContext>();
                options.Policies.AutoApplyTransactions();

                options.ListenAtPort(port).UseDurableInbox();
                options.PublishMessage<OnboardingRegistrationReadyIntegrationEvent>().ToPort(port).UseDurableOutbox();
            })
            .StartAsync();

    private AuthDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<AuthDbContext>().UseSqlServer(_connectionString).Options,
            new NoTenantContext()
        );

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

    /// <summary>Falla una vez el guardado que marca el correo como enviado.</summary>
    private sealed class FailOnceInterceptor : SaveChangesInterceptor
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
            var marking = eventData
                .Context!.ChangeTracker.Entries<TenantOnboarding>()
                .Any(entry => entry.State == EntityState.Modified);
            if (marking && Armed)
            {
                Armed = false;
                Interlocked.Increment(ref _failures);
                throw new TimeoutException("Simulated SQL timeout.");
            }
            return ValueTask.FromResult(result);
        }
    }
}

/// <summary>Lo que de verdad cruzó el puerto, por onboarding.</summary>
public static class RegistrationReadyProbeHandler
{
    private static readonly ConcurrentQueue<Guid> Received = new();

    public static int CountFor(Guid onboardingId) => Received.Count(id => id == onboardingId);

    public static void Handle(OnboardingRegistrationReadyIntegrationEvent message) =>
        Received.Enqueue(message.OnboardingId);
}
