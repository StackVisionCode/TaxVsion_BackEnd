using System.Net;
using System.Net.Sockets;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.Hosting;
using JasperFx;
using JasperFx.CodeGeneration.Model;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Application.Common;
using TaxVision.Postmaster.Application.Consumers;
using TaxVision.Postmaster.Application.Providers;
using TaxVision.Postmaster.Application.RateLimit;
using TaxVision.Postmaster.Application.Sending;
using TaxVision.Postmaster.Application.Sending.Commands.DispatchSentMessage;
using TaxVision.Postmaster.Application.Sending.Commands.MarkSendAttempted;
using TaxVision.Postmaster.Application.Sending.Commands.RecordSendResult;
using TaxVision.Postmaster.Application.Sending.Commands.ReleaseSendAttempt;
using TaxVision.Postmaster.Application.Sending.DomainEvents;
using TaxVision.Postmaster.Application.Suppression;
using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Infrastructure.Idempotency;
using TaxVision.Postmaster.Infrastructure.Persistence;
using TaxVision.Postmaster.Infrastructure.Persistence.Repositories;
using TaxVision.Postmaster.Tests.Consumers;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;
using Wolverine.Tracking;
using Wolverine.Transports.Tcp;

namespace TaxVision.Postmaster.Tests.Integration;

/// <summary>
/// PMS-07 contra SQL Server local, en una base temporal propia que se borra al terminar: nada queda en los esquemas
/// reales aunque la corrida se aborte, y la base de una corrida abortada se borra al arrancar. Remitentes falsos: nunca
/// sale un correo. Los avisos van a un puerto TCP cerrado y se cuentan en wolverine_outgoing_envelopes.
/// </summary>
public sealed class SentMessageDomainEventIntegrationTests : IAsyncLifetime
{
    private const string Prefix = "pmsbb1";
    private const string SucceededWire = "postmaster.email_delivery.succeeded";
    private const string FailedWire = "postmaster.email_delivery.failed";
    private const string SuppressedWire = "postmaster.email_delivery.suppressed";

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _connectionString;
    private readonly FakeSuppressionListRepository _suppression = new();
    private readonly ScriptedEmailSender _sender = new();
    private readonly FailFirstSaveAfterSend _failFirstSave;
    private IHost _host = null!;

    public SentMessageDomainEventIntegrationTests()
    {
        var baseConnection =
            Environment.GetEnvironmentVariable("POSTMASTER_DB_CONNECTION")
            ?? new ConfigurationBuilder()
                .AddUserSecrets("44669d5c-4874-4994-affe-1ca09fec4a42")
                .Build()
                .GetConnectionString("Default")
            ?? throw new InvalidOperationException("Set POSTMASTER_DB_CONNECTION or the Postmaster user-secrets.");
        _connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"{Prefix}_{Guid.NewGuid():N}"[..(Prefix.Length + 9)],
        }.ConnectionString;
        _failFirstSave = new FailFirstSaveAfterSend(() => _sender.SendCount > 0);
    }

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
    public async Task EmailAccepted_EmitsOneSucceeded()
    {
        await DeliverAsync(CreateEvent());

        Assert.Equal(1, _sender.SendCount);
        Assert.Equal(1, await WaitForOutgoingAsync(SucceededWire));
        Assert.Equal(nameof(SentMessageStatus.Sent), await SingleStatusAsync());
    }

    [Fact(Timeout = 120_000)]
    public async Task ProviderRefused_EmitsOneFailed()
    {
        _sender.Accept = false;

        await DeliverAsync(CreateEvent());

        Assert.Equal(1, await WaitForOutgoingAsync(FailedWire));
        Assert.Equal(0, await OutgoingAsync(SucceededWire));
        Assert.Equal(nameof(SentMessageStatus.Failed), await SingleStatusAsync());
    }

    [Fact(Timeout = 120_000)]
    public async Task AllRecipientsSuppressed_EmitsOneSuppressedAndNeverSends()
    {
        _suppression.SuppressedAddresses.Add("customer@example.test");

        await DeliverAsync(CreateEvent());

        Assert.Equal(0, _sender.SendCount);
        Assert.Equal(1, await WaitForOutgoingAsync(SuppressedWire));
    }

    // PMS-01 sigue en pie: si falla el registro después de que el proveedor aceptó, no se reenvía y sale un solo aviso.
    [Fact(Timeout = 120_000)]
    public async Task RecordFailsOnceAfterProviderAccepted_DoesNotResendAndEmitsOneNotice()
    {
        _failFirstSave.Enabled = true;

        await DeliverAsync(CreateEvent());

        Assert.True(_failFirstSave.Fired, "The simulated commit failure never happened.");
        Assert.Equal(1, _sender.SendCount);
        await WaitUntilAsync(async () => await OutgoingAsync(SucceededWire) + await OutgoingAsync(FailedWire) > 0);
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(1, await OutgoingAsync(SucceededWire) + await OutgoingAsync(FailedWire));
    }

    private NotificationsEmailSendRequestedIntegrationEvent CreateEvent() =>
        new()
        {
            TenantId = _tenantId,
            CorrelationId = "it-corr",
            NotificationLogId = Guid.NewGuid(),
            DispatchAttemptId = Guid.NewGuid(),
            IdempotencyKey = $"it:{Guid.NewGuid():N}",
            To = "customer@example.test",
            Subject = "Integration",
            HtmlBody = "<p>Hi</p>",
            TextBody = "Hi",
            TemplateKey = "it.template",
            RequiredProviderScope = "System",
            LogoScope = "System",
            Stream = "Transactional",
        };

    private async Task DeliverAsync(NotificationsEmailSendRequestedIntegrationEvent evt)
    {
        try
        {
            await _host
                .TrackActivity()
                .Timeout(TimeSpan.FromSeconds(60))
                .DoNotAssertOnExceptionsDetected()
                .PublishMessageAndWaitAsync(evt);
        }
        catch (Exception)
        {
            // Un intento que falla se reintenta o queda en la cola de errores, como en producción.
        }
    }

    private PostmasterDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PostmasterDbContext>().UseSqlServer(_connectionString).Options, new NoTenant());

    private async Task<string> SingleStatusAsync()
    {
        await using var db = NewContext();
        var message = await db.SentMessages.IgnoreQueryFilters().AsNoTracking().SingleAsync();
        return message.Status.ToString();
    }

    private async Task<int> OutgoingAsync(string messageType)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"IF OBJECT_ID('[wolverine].[wolverine_outgoing_envelopes]') IS NULL SELECT 0 ELSE SELECT COUNT(*) FROM [wolverine].[wolverine_outgoing_envelopes] WHERE message_type LIKE '%{messageType}%'";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<int> WaitForOutgoingAsync(string messageType)
    {
        await WaitUntilAsync(async () => await OutgoingAsync(messageType) > 0);
        return await OutgoingAsync(messageType);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline && !await condition())
            await Task.Delay(250);
    }

    private Task<IHost> StartHostAsync()
    {
        var closedPort = FreeTcpPort();
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddDbContext<PostmasterDbContext>(o =>
                    o.UseSqlServer(_connectionString).AddInterceptors(_failFirstSave)
                );
                services.AddSingleton<ITenantContext, NoTenant>();
                services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<PostmasterDbContext>());
                services.AddScoped<IIdempotencyGuard, SqlIdempotencyGuard>();
                services.AddScoped<ISentMessageRepository, SentMessageRepository>();
                services.AddSingleton<IProviderResolver>(
                    new FakeProviderResolver
                    {
                        ResolveReturnValue = new ResolveResult(
                            ProviderResolutionStatus.Resolved,
                            new("system-smtp", "localhost", 1025, false, null, null, "no-reply@example.test", "It", 60),
                            null,
                            TaxVision.Postmaster.Domain.Providers.ProviderScope.System
                        ),
                    }
                );
                services.AddSingleton<IConnectedMailboxResolver>(new FakeConnectedMailboxResolver());
                services.AddSingleton<ISuppressionListRepository>(_suppression);
                services.AddSingleton<IEmailProviderRateLimiter>(new FakeEmailProviderRateLimiter());
                services.AddSingleton<IEmailSender>(_sender);
                services.AddSingleton<IConnectedMailboxSender>(new FakeConnectedMailboxSender());
                services.AddSingleton<IInlineAssetFetcher>(new FakeInlineAssetFetcher());
                services.AddSingleton<IOutboundAttachmentFetcher>(new FakeOutboundAttachmentFetcher());
                services.AddSingleton<ITenantDirectoryRepository>(new FakeTenantDirectoryRepository());
                services.AddSingleton<ICorrelationContext>(new FakeCorrelationContext());
            })
            .UseWolverine(options =>
            {
                options.ServiceName = "postmaster-int-test";
                var discovery = options.Discovery.DisableConventionalDiscovery();
                discovery.IncludeType(typeof(NotificationsEmailSendRequestedConsumer));
                discovery.IncludeType(typeof(DispatchSentMessageHandler));
                discovery.IncludeType(typeof(MarkSendAttemptedHandler));
                discovery.IncludeType(typeof(ReleaseSendAttemptHandler));
                discovery.IncludeType(typeof(RecordSendResultHandler));
                discovery.IncludeType(typeof(SentMessageSentDomainEventHandler));
                discovery.IncludeType(typeof(SentMessageFailedDomainEventHandler));
                discovery.IncludeType(typeof(SentMessageSuppressedDomainEventHandler));

                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options.Durability.Mode = DurabilityMode.Solo;
                options.Durability.DrainTimeout = TimeSpan.FromSeconds(2);
                options.Durability.KeepAfterMessageHandling = TimeSpan.FromMinutes(5);
                options.PersistMessagesWithSqlServer(_connectionString, "wolverine");
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options.Policies.UseDurableOutboxOnAllSendingEndpoints();
                options
                    .UseEntityFrameworkCoreTransactions()
                    .WithDbContextAbstraction<IUnitOfWork, PostmasterDbContext>();
                options.Policies.AutoApplyTransactions();
                options.ApplyStandardFailurePolicies();
                options.LocalQueueFor<DispatchSentMessageCommand>().UseDurableInbox();
                options.UseTaxVisionDomainEvents();
                options.LocalQueue(DomainEventDispatchExtensions.DomainEventsQueue).Sequential();
                options.UseTaxVisionDurableLocalQueues();

                options
                    .PublishMessage<PostmasterEmailDeliverySucceededIntegrationEvent>()
                    .ToPort(closedPort)
                    .UseDurableOutbox();
                options
                    .PublishMessage<PostmasterEmailDeliveryFailedIntegrationEvent>()
                    .ToPort(closedPort)
                    .UseDurableOutbox();
                options
                    .PublishMessage<PostmasterEmailDeliverySuppressedIntegrationEvent>()
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

        public void SetTenant(Guid tenantId) { }
    }

    /// <summary>Remitente falso: acepta o rechaza según la prueba, y cuenta las llamadas.</summary>
    private sealed class ScriptedEmailSender : IEmailSender
    {
        private int _sendCount;
        public int SendCount => _sendCount;
        public bool Accept { get; set; } = true;

        public Task<SendResult> SendAsync(
            SentMessage message,
            RenderedContent content,
            ResolvedEmailProvider provider,
            IReadOnlyList<InlineAssetBytes> inlineAssets,
            IReadOnlyList<OutboundAttachmentBytes> attachments,
            CancellationToken ct
        )
        {
            Interlocked.Increment(ref _sendCount);
            return Task.FromResult(
                Accept
                    ? new SendResult(true, $"it-{Guid.NewGuid():N}", null, [])
                    : new SendResult(false, null, "Provider refused the message.", [])
            );
        }
    }

    /// <summary>Hace fallar una sola vez la primera confirmación posterior al envío.</summary>
    private sealed class FailFirstSaveAfterSend(Func<bool> sent) : SaveChangesInterceptor
    {
        private int _fired;
        public bool Enabled { get; set; }
        public bool Fired => _fired == 1;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        ) =>
            Enabled && sent() && Interlocked.CompareExchange(ref _fired, 1, 0) == 0
                ? throw new InvalidOperationException("Simulated commit failure.")
                : base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
