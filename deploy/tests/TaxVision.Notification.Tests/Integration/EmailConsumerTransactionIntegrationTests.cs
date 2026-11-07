using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Platform;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using JasperFx.CodeGeneration.Model;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Infrastructure.Messaging;
using TaxVision.Notification.Infrastructure.Persistence;
using TaxVision.Notification.Infrastructure.Persistence.Repositories;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.RabbitMQ;
using Wolverine.SqlServer;

namespace TaxVision.Notification.Tests.Integration;

/// <summary>
/// Contra el SQL Server local, con la misma configuración transaccional de Wolverine que Program.cs: un
/// consumer de correo que falla después de encolar el primer correo no deja NotificationLog ni sobre. El
/// outbox va a un esquema propio que se borra al terminar; los transportes están simulados (nada sale).
/// </summary>
public sealed class EmailConsumerTransactionIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _schema = $"notif_tint_{Guid.NewGuid():N}"[..20];
    private readonly FlakyScribe _scribe = new();
    private IHost _host = null!;

    private static readonly string ConnectionString =
        NotificationIntegrationDatabase.ConnectionString;

    public async Task InitializeAsync()
    {
        _host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddDbContext<NotificationDbContext>(o => o.UseSqlServer(ConnectionString));
                services.AddScoped<IUnitOfWork>(p => p.GetRequiredService<NotificationDbContext>());
                services.AddSingleton<ITenantContext>(new FixedTenant(_tenantId));
                services.AddScoped<INotificationLogRepository, NotificationLogRepository>();
                services.AddScoped<HandlerBus>();
                services.AddScoped<IIntegrationEventPublisher, WolverineIntegrationEventPublisher>();
                services.AddScoped<IUserNotificationPreferenceRepository, UserNotificationPreferenceRepository>();
                services.AddScoped<IUserEmailDirectoryRepository, UserEmailDirectoryRepository>();
                services.AddScoped<NotificationDeliveryGate>();
                services.AddScoped<IEmailDispatchGateway, EventBasedEmailDispatchGateway>();
                services.AddSingleton<IScribeRenderClient>(_scribe);
                services.AddSingleton<ITenantHostResolver>(new FakeTenantHostResolver("acme.test"));
                services.AddSingleton<IPlatformSettings>(TestPlatform.Settings);
                services.AddSingleton<ICorrelationContext>(new FakeCorrelation());
                services.AddSingleton(Options.Create(new PortalOptions { BaseUrl = "https://app.test" }));
            })
            .UseWolverine(options =>
            {
                options.Discovery.DisableConventionalDiscovery().IncludeType(typeof(EmailChangeRequestedConsumer));
                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options.Durability.Mode = DurabilityMode.Solo;
                options.UseRabbitMq(new Uri("amqp://localhost"));
                options.PersistMessagesWithSqlServer(ConnectionString, _schema);
                options.Policies.UseDurableOutboxOnAllSendingEndpoints();
                options
                    .UseEntityFrameworkCoreTransactions()
                    .WithDbContextAbstraction<IUnitOfWork, NotificationDbContext>();
                options.Policies.AutoApplyTransactions();
                options
                    .PublishMessage<NotificationsEmailSendRequestedIntegrationEvent>()
                    .ToRabbitExchange("taxvision-events");
                options.StubAllExternalTransports();
            })
            .StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();

        await using (var db = NewContext())
        {
            await db
                .NotificationDispatchAttempts.IgnoreQueryFilters()
                .Where(a =>
                    db.NotificationLogs.IgnoreQueryFilters()
                        .Any(l => l.Id == a.NotificationLogId && l.TenantId == _tenantId)
                )
                .ExecuteDeleteAsync();
            await db.NotificationLogs.IgnoreQueryFilters().Where(l => l.TenantId == _tenantId).ExecuteDeleteAsync();
        }

        await DropTestSchemaAsync();
    }

    [Fact]
    public async Task FailureAfterQueueEmail_LeavesNoLogAndNoEnvelope()
    {
        var evt = NewEvent();
        _scribe.FailOnRender = 2;

        var bus = _host.Services.GetRequiredService<IMessageBus>();
        await Assert.ThrowsAnyAsync<Exception>(() => bus.InvokeAsync(evt));

        await using var db = NewContext();
        Assert.False(
            await db.NotificationLogs.IgnoreQueryFilters().AnyAsync(l => l.RelatedEventId == evt.EventId),
            "El NotificationLog del primer destinatario debe revertirse junto con el fallo."
        );
        Assert.Equal(0, await CountEmailEnvelopesAsync());
    }

    private EmailChangeRequestedIntegrationEvent NewEvent() =>
        new()
        {
            TenantId = _tenantId,
            UserId = Guid.NewGuid(),
            CurrentEmail = "old@example.test",
            NewEmail = "new@example.test",
            RawToken = "tok",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            ActorType = "TenantEmployee",
            TenantName = "Acme",
        };

    private NotificationDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<NotificationDbContext>().UseSqlServer(ConnectionString).Options,
            new FixedTenant(_tenantId)
        );

    private async Task<int> CountEmailEnvelopesAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            $"SELECT COUNT(*) FROM [{_schema}].[wolverine_outgoing_envelopes] WHERE message_type LIKE '%EmailSendRequested%'",
            connection
        );
        return (int)(await command.ExecuteScalarAsync())!;
    }

    // Borra todo lo que Wolverine creó en el esquema de prueba y el esquema mismo.
    private async Task DropTestSchemaAsync()
    {
        if (!_schema.StartsWith("notif_tint_", StringComparison.Ordinal))
            return;

        const string sql = """
            DECLARE @sql nvarchar(max) = N'';
            DECLARE @sid int = SCHEMA_ID(@schema);
            IF @sid IS NULL RETURN;
            SELECT @sql += N'ALTER TABLE ' + QUOTENAME(@schema) + N'.' + QUOTENAME(OBJECT_NAME(parent_object_id))
                + N' DROP CONSTRAINT ' + QUOTENAME(name) + N';' FROM sys.foreign_keys WHERE schema_id = @sid;
            SELECT @sql += N'DROP PROCEDURE ' + QUOTENAME(@schema) + N'.' + QUOTENAME(name) + N';'
                FROM sys.procedures WHERE schema_id = @sid;
            SELECT @sql += N'DROP FUNCTION ' + QUOTENAME(@schema) + N'.' + QUOTENAME(name) + N';'
                FROM sys.objects WHERE schema_id = @sid AND type IN ('FN', 'IF', 'TF');
            SELECT @sql += N'DROP TABLE ' + QUOTENAME(@schema) + N'.' + QUOTENAME(name) + N';'
                FROM sys.tables WHERE schema_id = @sid;
            SELECT @sql += N'DROP SEQUENCE ' + QUOTENAME(@schema) + N'.' + QUOTENAME(name) + N';'
                FROM sys.sequences WHERE schema_id = @sid;
            SELECT @sql += N'DROP TYPE ' + QUOTENAME(@schema) + N'.' + QUOTENAME(name) + N';'
                FROM sys.types WHERE is_user_defined = 1 AND schema_id = @sid;
            SET @sql += N'DROP SCHEMA ' + QUOTENAME(@schema) + N';';
            EXEC sp_executesql @sql;
            """;

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@schema", _schema);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }

    // Responde bien hasta el render número FailOnRender, que lanza (fallo después del primer correo).
    private sealed class FlakyScribe : IScribeRenderClient
    {
        public Task<Result<ScribeRenderedText>> RenderTextAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        ) => TestDoubles.ScribeTestDefaults.TextUnsupported();

        private int _renders;
        public int? FailOnRender { get; set; }

        public Task<Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        ) => RenderAsync(eventKey, tenantId, variables, ScribeRenderAudience.Staff, ct);

        public Task<Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            ScribeRenderAudience audience,
            CancellationToken ct = default
        )
        {
            var current = Interlocked.Increment(ref _renders);
            if (FailOnRender is { } failAt && current == failAt)
                throw new InvalidOperationException("Scribe unavailable (test).");
            return Task.FromResult(Result.Success(new ScribeRenderedEmail("subject", "<p>body</p>", "body")));
        }
    }

    private sealed class FakeCorrelation : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = "test";

        public void Set(string correlationId) => CorrelationId = correlationId;

        public IDisposable Push(string correlationId)
        {
            CorrelationId = correlationId;
            return new Scope();
        }

        private sealed class Scope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
