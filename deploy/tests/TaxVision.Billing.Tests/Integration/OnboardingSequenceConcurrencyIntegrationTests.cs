using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.BillingIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Platform;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.Common;
using BuildingBlocks.Web.Tenancy;
using JasperFx;
using JasperFx.CodeGeneration.Model;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.GenerateInvoicePdf;
using TaxVision.Billing.Application.Invoices.IntegrationEvents;
using TaxVision.Billing.Infrastructure.Messaging;
using TaxVision.Billing.Infrastructure.Persistence;
using TaxVision.Billing.Infrastructure.Persistence.Repositories;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;
using Xunit;

namespace TaxVision.Billing.Tests.Integration;

/// <summary>
/// BIL-11 con SQL real y el consumidor real. Va en una base temporal propia porque la secuencia es la de la
/// plataforma: la base local no se toca. Se borra al arrancar, por si una corrida abortada la dejó, y al terminar.
/// </summary>
public sealed class OnboardingSequenceConcurrencyIntegrationTests : IAsyncLifetime
{
    private const string DatabaseName = "billing_bil11_sequence_it";
    private const int Onboardings = 10;

    private static readonly SqlConnectionStringBuilder Server = new(
        Environment.GetEnvironmentVariable("BILLING_DB_CONNECTION")
            ?? new ConfigurationBuilder()
                .AddUserSecrets("e4b1c9d2-7f3a-4a86-9b2d-billing000001")
                .Build()
                .GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Set BILLING_DB_CONNECTION or the Billing user-secrets to run this test."
            )
    );

    private static readonly string Connection = new SqlConnectionStringBuilder(Server.ConnectionString)
    {
        InitialCatalog = DatabaseName,
    }.ConnectionString;

    private IHost _host = null!;

    public async Task InitializeAsync()
    {
        await DropDatabaseAsync();
        await using (var db = NewContext())
            await db.Database.MigrateAsync();
        _host = await StartHostAsync();
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        SqlConnection.ClearAllPools();
        await DropDatabaseAsync();
    }

    [Fact(Timeout = 120_000)]
    public async Task TenParallelAllocations_AllSucceedWithDistinctNumbers()
    {
        var bus = _host.Services.GetRequiredService<IMessageBus>();
        for (var i = 0; i < Onboardings; i++)
            await bus.PublishAsync(Requested());

        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline && await SettledAsync() < Onboardings)
            await Task.Delay(250);

        Assert.Equal(0, await CountDeadLettersAsync());
        await using var db = NewContext();
        var numbers = await db.Invoices.IgnoreQueryFilters().Select(i => i.InvoiceNumber!).ToListAsync();
        var year = DateTime.UtcNow.Year;
        Assert.Equal(
            Enumerable.Range(1, Onboardings).Select(n => $"ONB-{year}-{n:D5}"),
            numbers.OrderBy(number => number, StringComparer.Ordinal)
        );

        // Lo que de verdad se entregó, no lo que hay en la tabla: un intento repetido no deja PDFs de más.
        var pdfDeadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < pdfDeadline && PdfRequestProbeHandler.Received < Onboardings)
            await Task.Delay(100);
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(Onboardings, PdfRequestProbeHandler.Received);
    }

    private static OnboardingInvoiceRequestedIntegrationEvent Requested() =>
        new()
        {
            TenantId = PlatformTenant.Id,
            OnboardingId = Guid.NewGuid(),
            PlanId = Guid.NewGuid(),
            PlanDescription = "Enterprise (Monthly)",
            PayerName = "Owner",
            PaymentId = Guid.NewGuid(),
            GrossAmountCents = 4900,
            DiscountAmountCents = 0,
            NetAmountCents = 4900,
            Currency = "USD",
            SettlementType = "Paid",
        };

    private static Task<IHost> StartHostAsync() =>
        Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                options.ServiceName = "bil11-sequence-test";
                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options
                    .Discovery.DisableConventionalDiscovery()
                    .IncludeType(typeof(OnboardingInvoiceRequestedConsumer))
                    .IncludeType(typeof(PdfRequestProbeHandler));
                options.PersistMessagesWithSqlServer(Connection, "integration");
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options.Durability.Mode = DurabilityMode.Solo;
                options.Durability.ScheduledJobPollingTime = TimeSpan.FromSeconds(1);

                options.Services.AddBuildingBlocks();
                options.Services.AddDbContext<BillingDbContext>(db => db.UseSqlServer(Connection));
                options.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<BillingDbContext>());
                options.Services.AddScoped<IInvoiceRepository, InvoiceRepository>();
                options.Services.AddScoped<IInvoiceNumberSequenceRepository, InvoiceNumberSequenceRepository>();
                options.Services.AddScoped<IOnboardingTenantMapRepository, OnboardingTenantMapRepository>();
                options.Services.AddSingleton<IPlatformSettings>(new PlatformStub());
                options.Services.AddSingleton(TimeProvider.System);
                options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, BillingDbContext>();
                options.Policies.AutoApplyTransactions();
                options
                    .Policies.ForMessagesOfType<IIntegrationEvent>()
                    .AddMiddleware(typeof(IntegrationEventTenantMiddleware));

                options
                    .PublishMessage<OnboardingInvoiceRequestedIntegrationEvent>()
                    .ToLocalQueue("onboarding-sequence-test")
                    .MaximumParallelMessages(Onboardings)
                    .UseDurableInbox();

                options.Policies.Add(new OnboardingSequenceRetryPolicy(new OnboardingSequenceRetryOptions()));
                options.UseTaxVisionConflictPolicy();
                options.ApplyStandardFailurePolicies();
            })
            .StartAsync();

    private static BillingDbContext NewContext() =>
        new(new DbContextOptionsBuilder<BillingDbContext>().UseSqlServer(Connection).Options, new AnyTenant());

    private static async Task<int> SettledAsync()
    {
        await using var db = NewContext();
        return await db.Invoices.IgnoreQueryFilters().CountAsync() + await CountDeadLettersAsync();
    }

    private static async Task<int> CountDeadLettersAsync()
    {
        await using var connection = new SqlConnection(Connection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM integration.wolverine_dead_letters";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task DropDatabaseAsync()
    {
        var master = new SqlConnectionStringBuilder(Server.ConnectionString) { InitialCatalog = "master" };
        await using var connection = new SqlConnection(master.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID(N'{DatabaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{DatabaseName}];
            END
            """;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class AnyTenant : ITenantContext
    {
        public Guid TenantId => PlatformTenant.Id;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }

    private sealed class PlatformStub : IPlatformSettings
    {
        public PlatformSettingsSnapshot Current { get; } =
            new(1, "Brand", "Brand LLC", "brand.test", "api.brand.test", "help@brand.test", "billing@brand.test");
    }
}

/// <summary>Cuenta los pedidos de PDF que llegan de verdad al handler.</summary>
public static class PdfRequestProbeHandler
{
    private static int _received;

    public static int Received => Volatile.Read(ref _received);

    public static void Handle(GenerateInvoicePdfCommand _) => Interlocked.Increment(ref _received);
}
