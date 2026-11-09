using System.Net;
using System.Net.Sockets;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.CorrespondenceIntegrationEvents;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.Common;
using BuildingBlocks.Web.Hosting;
using JasperFx;
using JasperFx.CodeGeneration.Model;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Delivery;
using TaxVision.Correspondence.Application.Inbox.DomainEvents;
using TaxVision.Correspondence.Application.Ingest;
using TaxVision.Correspondence.Application.Threads;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.Projections;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Infrastructure.Persistence;
using TaxVision.Correspondence.Infrastructure.Persistence.Repositories;
using TaxVision.Correspondence.Tests.Ingest;
using TaxVision.Correspondence.Tests.Search;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;
using Wolverine.Transports.Tcp;

namespace TaxVision.Correspondence.Tests.Integration;

/// <summary>
/// COR-11 y COR-14 contra SQL Server local, en una base temporal propia que se borra al terminar: nada queda en los
/// esquemas reales aunque la corrida se aborte, y la base de una corrida abortada se borra al arrancar. Host mínimo
/// de Wolverine con los handlers reales; los avisos van a un puerto TCP cerrado y se cuentan en la outbox.
/// </summary>
public sealed class CorrespondenceDomainEventIntegrationTests : IAsyncLifetime
{
    private const string Prefix = "corbb1";

    // Estos contratos viajan con su alias, no con el nombre del tipo.
    private const string EmailReceivedWire = "correspondence.customer_email_received";
    private const string ThreadStateWire = "correspondence.thread_state_changed";

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly string _connectionString = new SqlConnectionStringBuilder(CorrespondenceSqlServer.ConnectionString)
    {
        InitialCatalog = $"{Prefix}_{Guid.NewGuid():N}"[..(Prefix.Length + 9)],
    }.ConnectionString;
    private readonly FailAfterSaveInterceptor _failAfterSave = new();
    private IHost _host = null!;

    public async Task InitializeAsync()
    {
        await DropOrphansAsync();
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
    public async Task Ingest_EmailAndNoticeCommitTogether()
    {
        await SeedCustomerAsync();

        await InvokeAsync(NewMessage("<a@example.test>"));

        Assert.Equal(1, await CountEmailsAsync());
        Assert.Equal(1, await WaitForOutgoingAsync(EmailReceivedWire));
        Assert.Equal(0, await OutgoingAsync(ThreadStateWire));
    }

    [Fact(Timeout = 120_000)]
    public async Task Ingest_FailureAfterSave_LeavesNeitherEmailNorEnvelope()
    {
        await SeedCustomerAsync();
        _failAfterSave.Always = true; // los reintentos en línea también fallan

        await Assert.ThrowsAnyAsync<Exception>(() => InvokeAsync(NewMessage("<b@example.test>")));

        Assert.Equal(0, await CountEmailsAsync());
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(0, await CountEnvelopesAsync("wolverine_incoming_envelopes", "IncomingEmailReceived"));
        Assert.Equal(0, await OutgoingAsync(EmailReceivedWire));
    }

    [Fact(Timeout = 120_000)]
    public async Task ReplyToArchivedThread_EmitsOneOfEachNotice()
    {
        await SeedCustomerAsync();
        var thread = await SeedArchivedThreadAsync("T1");

        await InvokeAsync(NewMessage("<c@example.test>", providerThreadId: "T1"));

        Assert.Equal(1, await WaitForOutgoingAsync(EmailReceivedWire));
        Assert.Equal(1, await WaitForOutgoingAsync(ThreadStateWire));
        await using var db = NewContext();
        var saved = await db.EmailThreads.IgnoreQueryFilters().AsNoTracking().SingleAsync(t => t.Id == thread.Id);
        Assert.Equal(EmailThreadStatus.Active, saved.Status);
    }

    [Fact(Timeout = 120_000)]
    public async Task ReplayIntoArchivedThread_ReopensWithoutNotices()
    {
        await SeedCustomerAsync();
        await SeedArchivedThreadAsync("T1");

        await InvokeAsync(NewMessage("<d@example.test>", providerThreadId: "T1") with { IsReplay = true });

        Assert.Equal(1, await CountEmailsAsync());
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(0, await OutgoingAsync(EmailReceivedWire));
        Assert.Equal(0, await OutgoingAsync(ThreadStateWire));
    }

    [Fact(Timeout = 120_000)]
    public async Task ArchiveThread_FromAHandler_EmitsOneNotice()
    {
        var thread = await SeedActiveThreadAsync();

        await _host
            .Services.GetRequiredService<IMessageBus>()
            .InvokeAsync<BuildingBlocks.Results.Result>(
                new ArchiveThreadCommand(_tenantId, thread.Id, ActorUserId: Guid.NewGuid())
            );

        Assert.Equal(1, await WaitForOutgoingAsync(ThreadStateWire));
    }

    // COR-14: el aviso adelantado espera en el inbox durable y no se pierde cuando el borrador llega a Sent.
    [Fact(Timeout = 120_000)]
    public async Task EarlyDeliveryNotice_WaitsInTheDurableInboxAndIsAppliedWhenTheDraftIsSent()
    {
        var draft = MailSearchTestData.Open(_tenantId, null, Guid.NewGuid(), "Quote", "maria@x.com");
        draft.MarkSending();
        await using (var db = NewContext())
        {
            db.Drafts.Add(draft);
            await db.SaveChangesAsync();
        }

        await _host
            .Services.GetRequiredService<IMessageBus>()
            .PublishAsync(
                new PostmasterCorrespondenceDeliveryFeedbackIntegrationEvent
                {
                    TenantId = _tenantId,
                    CorrelationId = "corr-early",
                    SentMessageId = Guid.NewGuid(),
                    CorrespondenceDraftId = draft.Id,
                    Recipient = "maria@x.com",
                    Outcome = CorrespondenceDeliveryOutcomes.NotDelivered,
                    Category = DeliveryFailureCategories.InvalidAddress,
                    StatusCode = "5.1.1",
                    DiagnosticSnippet = "550 5.1.1 user unknown",
                    Suppressed = true,
                    EventAtUtc = DateTime.UtcNow,
                }
            );

        // A medio camino: el sobre sigue guardado y programado, sin aviso registrado.
        Assert.True(
            await WaitUntilAsync(async () =>
                await CountEnvelopesAsync(
                    "wolverine_incoming_envelopes",
                    "postmaster.correspondence_delivery_feedback",
                    "status = 'Scheduled'"
                ) == 1
            )
        );
        Assert.Equal(0, await CountNoticesAsync());

        await using (var db = NewContext())
        {
            var saved = await db.Drafts.IgnoreQueryFilters().SingleAsync(d => d.Id == draft.Id);
            saved.MarkSent(Guid.NewGuid());
            await db.SaveChangesAsync();
        }

        Assert.True(await WaitUntilAsync(async () => await CountNoticesAsync() == 1));
    }

    private Task InvokeAsync(
        BuildingBlocks.Messaging.EmailIntegrationEvents.ConnectorsRawMessageReceivedIntegrationEvent evt
    ) => _host.Services.GetRequiredService<IMessageBus>().InvokeAsync(evt);

    private BuildingBlocks.Messaging.EmailIntegrationEvents.ConnectorsRawMessageReceivedIntegrationEvent NewMessage(
        string internetMessageId,
        string? providerThreadId = null
    ) =>
        new()
        {
            TenantId = _tenantId,
            CorrelationId = Guid.NewGuid().ToString(),
            AccountId = Guid.NewGuid(),
            MailboxOwnershipKnown = true,
            ProviderCode = "gmail",
            ProviderMessageId = Guid.NewGuid().ToString(),
            ProviderThreadId = providerThreadId,
            InternetMessageId = internetMessageId,
            From = "customer@example.test",
            To = ["office@example.test"],
            Subject = "Subject",
            Snippet = "Snippet",
            ReceivedAtUtc = DateTime.UtcNow,
            HasAttachments = false,
            AttachmentCount = 0,
            SpfResult = "Pass",
            DkimResult = "Pass",
            DmarcResult = "Pass",
        };

    private async Task SeedCustomerAsync()
    {
        await using var db = NewContext();
        db.CustomerEmailAddresses.Add(
            CustomerEmailAddress.Create(_tenantId, _customerId, EmailAddress.Create("customer@example.test").Value)
        );
        await db.SaveChangesAsync();
    }

    private async Task<EmailThread> SeedActiveThreadAsync()
    {
        var thread = EmailThread.NewFromMessage(_tenantId, _customerId, "Subject", null, DateTime.UtcNow).Value;
        await using var db = NewContext();
        db.EmailThreads.Add(thread);
        await db.SaveChangesAsync();
        return thread;
    }

    private async Task<EmailThread> SeedArchivedThreadAsync(string providerThreadId)
    {
        var thread = EmailThread
            .NewFromMessage(_tenantId, _customerId, "Subject", providerThreadId, DateTime.UtcNow.AddDays(-1))
            .Value;
        thread.Archive();
        await using var db = NewContext();
        db.EmailThreads.Add(thread);
        await db.SaveChangesAsync();
        return thread;
    }

    private async Task<int> CountEmailsAsync()
    {
        await using var db = NewContext();
        return await db.IncomingEmails.IgnoreQueryFilters().CountAsync(e => e.TenantId == _tenantId);
    }

    private async Task<int> CountNoticesAsync()
    {
        await using var db = NewContext();
        return await db.DeliveryNotices.IgnoreQueryFilters().CountAsync(n => n.TenantId == _tenantId);
    }

    private CorrespondenceDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<CorrespondenceDbContext>().UseSqlServer(_connectionString).Options,
            new NoTenant()
        );

    private Task<int> OutgoingAsync(string messageType) =>
        CountEnvelopesAsync("wolverine_outgoing_envelopes", messageType);

    private async Task<int> CountEnvelopesAsync(string table, string messageType, string filter = "1 = 1")
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"IF OBJECT_ID('[wolverine].[{table}]') IS NULL SELECT 0 ELSE SELECT COUNT(*) FROM [wolverine].[{table}] WHERE message_type LIKE '%{messageType}%' AND {filter}";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<int> WaitForOutgoingAsync(string messageType)
    {
        await WaitUntilAsync(async () => await OutgoingAsync(messageType) > 0);
        return await OutgoingAsync(messageType);
    }

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return true;
            await Task.Delay(250);
        }
        return false;
    }

    private Task<IHost> StartHostAsync()
    {
        var closedPort = FreeTcpPort();
        return Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                options.ServiceName = "correspondence-int-test";
                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options.Discovery.DisableConventionalDiscovery();
                options.PersistMessagesWithSqlServer(_connectionString, "wolverine");
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options.Durability.Mode = DurabilityMode.Solo;
                options.Durability.ScheduledJobFirstExecution = TimeSpan.FromMilliseconds(100);
                options.Durability.ScheduledJobPollingTime = TimeSpan.FromSeconds(1);
                options.Durability.DrainTimeout = TimeSpan.FromSeconds(2);
                options.Durability.KeepAfterMessageHandling = TimeSpan.FromMinutes(5);

                options.Services.AddBuildingBlocks();
                options.Services.AddDbContext<CorrespondenceDbContext>(db =>
                    db.UseSqlServer(_connectionString).AddInterceptors(_failAfterSave)
                );
                options.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CorrespondenceDbContext>());
                options.Services.AddScoped<IIncomingEmailRepository, IncomingEmailRepository>();
                options.Services.AddScoped<IEmailThreadRepository, EmailThreadRepository>();
                options.Services.AddScoped<ICustomerEmailAddressRepository, CustomerEmailAddressRepository>();
                options.Services.AddScoped<IUnmatchedIncomingEmailRepository, UnmatchedIncomingEmailRepository>();
                options.Services.AddScoped<IDraftRepository, DraftRepository>();
                options.Services.AddScoped<IDeliveryNoticeRepository, DeliveryNoticeRepository>();
                options.Services.AddScoped<ThreadResolver>();
                options.Services.AddScoped<IngestRejectionRecorder>();
                options.Services.AddScoped<IncomingSenderResolver>();
                options.Services.AddScoped<IncomingMailIngestor>();
                options.Services.AddSingleton<IIngestMetrics, FakeIngestMetrics>();
                options.Services.AddSingleton(Options.Create(new CorrespondenceIngestOptions()));

                options
                    .UseEntityFrameworkCoreTransactions()
                    .WithDbContextAbstraction<IUnitOfWork, CorrespondenceDbContext>();
                options.Policies.AutoApplyTransactions();
                options.Policies.AddMiddleware(typeof(BuildingBlocks.Web.Tenancy.LocalCommandTenantMiddleware));
                // La primera espera es larga: da tiempo a medir el sobre programado antes del reintento.
                options.ApplyStandardFailurePolicies(
                    new DependencyNotReadyRetryOptions
                    {
                        Delays = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)],
                    }
                );

                options.Discovery.IncludeType(typeof(RawMessageReceivedConsumer));
                options.Discovery.IncludeType(typeof(PostmasterCorrespondenceDeliveryFeedbackConsumer));
                options.Discovery.IncludeType(typeof(ArchiveThreadHandler));
                options.Discovery.IncludeType(typeof(IncomingEmailReceivedDomainEventHandler));
                options.Discovery.IncludeType(typeof(EmailThreadStateChangedDomainEventHandler));
                options.UseTaxVisionDomainEvents();
                options.LocalQueue(DomainEventDispatchExtensions.DomainEventsQueue).Sequential();
                options.UseTaxVisionDurableLocalQueues();

                options
                    .PublishMessage<CorrespondenceCustomerEmailReceivedIntegrationEvent>()
                    .ToPort(closedPort)
                    .UseDurableOutbox();
                options
                    .PublishMessage<CorrespondenceThreadStateChangedIntegrationEvent>()
                    .ToPort(closedPort)
                    .UseDurableOutbox();
                options
                    .PublishMessage<CorrespondenceDeliveryNoticeReceivedIntegrationEvent>()
                    .ToPort(closedPort)
                    .UseDurableOutbox();
            })
            .StartAsync();
    }

    // Una corrida abortada no llama DisposeAsync: su base temporal se borra aquí.
    private async Task DropOrphansAsync()
    {
        var master = new SqlConnectionStringBuilder(_connectionString) { InitialCatalog = "master" }.ConnectionString;
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            DECLARE @sql nvarchar(max) = N'';
            SELECT @sql += N'ALTER DATABASE ' + QUOTENAME(name) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ' + QUOTENAME(name) + N';'
              FROM sys.databases WHERE name LIKE N'{Prefix}[_]%';
            EXEC sp_executesql @sql;
            """;
        await command.ExecuteNonQueryAsync();
    }

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

    private sealed class FailAfterSaveInterceptor : SaveChangesInterceptor
    {
        public bool Always { get; set; }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            if (!Always)
                return ValueTask.FromResult(result);
            throw new InvalidOperationException("Fallo forzado después de guardar.");
        }
    }
}
