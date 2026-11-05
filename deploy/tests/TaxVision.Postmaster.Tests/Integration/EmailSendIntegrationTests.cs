using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
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

namespace TaxVision.Postmaster.Tests.Integration;

/// <summary>
/// Contra el SQL Server real (user-secrets de Postmaster o POSTMASTER_DB_CONNECTION): host mínimo de
/// Wolverine con las transacciones de Program.cs y remitentes falsos (nunca sale un correo). Las tablas
/// de Wolverine van en un esquema propio; las filas del tenant de la prueba y el esquema se borran al final.
/// </summary>
public sealed class EmailSendIntegrationTests : IAsyncLifetime
{
    private const string Schema = "pms_email_send_it";
    private readonly Guid _tenantId = Guid.NewGuid();

    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("POSTMASTER_DB_CONNECTION")
        ?? new ConfigurationBuilder()
            .AddUserSecrets("44669d5c-4874-4994-affe-1ca09fec4a42")
            .Build()
            .GetConnectionString("Default")
        ?? throw new InvalidOperationException(
            "Set POSTMASTER_DB_CONNECTION or the Postmaster user-secrets to run this test."
        );

    // Los handlers del envío que levanta el host mínimo.
    private static readonly Type[] HandlerTypes =
    [
        typeof(NotificationsEmailSendRequestedConsumer),
        typeof(DispatchSentMessageHandler),
        typeof(MarkSendAttemptedHandler),
        typeof(ReleaseSendAttemptHandler),
        typeof(RecordSendResultHandler),
    ];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM SentMessageEvents WHERE TenantId = @tenant;
                DELETE FROM DeliveryAttempts WHERE TenantId = @tenant;
                DELETE FROM SentMessageRecipients WHERE TenantId = @tenant;
                DELETE FROM SentMessages WHERE TenantId = @tenant;
                DELETE FROM EmailIdempotency WHERE TenantId = @tenant;
                """;
            command.Parameters.AddWithValue("@tenant", _tenantId);
            await command.ExecuteNonQueryAsync();
        }
        await DropWolverineSchemaAsync();
    }

    [Fact]
    public async Task FailureAfterProviderAccepted_DoesNotResend()
    {
        var sender = new CountingEmailSender();
        var interceptor = new FailFirstSaveAfterSend(() => sender.SendCount > 0);
        var evt = CreateEvent();

        using (var host = await StartHostAsync(sender, interceptor))
        {
            await DeliverAsync(host, evt);
            // Notification (o el broker) vuelve a entregar el mismo evento.
            await DeliverAsync(host, evt);
            await host.StopAsync(TimeSpan.FromSeconds(10));
        }

        Assert.True(interceptor.Fired, "The simulated commit failure never happened.");
        Assert.Equal(1, sender.SendCount);
        Assert.Equal(1, await CountAsync("SentMessages"));
        Assert.Equal(1, await CountAsync("EmailIdempotency"));
        // El intento quedó confirmado aunque el resultado falló al guardarse. Lo cierra el reintento del
        // registro (Sent) o, si se agota, el del despacho como desconocido; nunca queda en Sending.
        var (status, attemptedAt, errorReason) = await ReadSentMessageAsync();
        Assert.NotNull(attemptedAt);
        Assert.True(
            status == nameof(SentMessageStatus.Sent)
                || (
                    status == nameof(SentMessageStatus.Failed)
                    && errorReason?.StartsWith(SendOutcomeReasons.SendOutcomeUnknown) == true
                ),
            $"Unexpected final state: {status} ({errorReason})."
        );
    }

    [Fact]
    public async Task ReservationIsVisibleToAnotherContextBeforeTheSend()
    {
        var sender = new GateEmailSender();
        var evt = CreateEvent();

        using var host = await StartHostAsync(sender, interceptor: null);
        var delivery = DeliverAsync(host, evt);
        await sender.Entered.Task.WaitAsync(TimeSpan.FromSeconds(60));

        var visible = await ReservationIsVisibleAsync(evt.IdempotencyKey);

        sender.Release();
        await delivery;
        await host.StopAsync(TimeSpan.FromSeconds(10));

        Assert.True(visible, "The idempotency reservation was not committed before the email went out.");
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

    private static async Task DeliverAsync(IHost host, NotificationsEmailSendRequestedIntegrationEvent evt)
    {
        try
        {
            await host.TrackActivity()
                .Timeout(TimeSpan.FromSeconds(90))
                .DoNotAssertOnExceptionsDetected()
                .PublishMessageAndWaitAsync(evt);
        }
        catch (Exception)
        {
            // Un intento que falla se reintenta o queda en la cola de errores, como en producción.
        }
    }

    private async Task<IHost> StartHostAsync(IEmailSender sender, SaveChangesInterceptor? interceptor)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddDbContext<PostmasterDbContext>(o =>
                {
                    o.UseSqlServer(_connectionString);
                    if (interceptor is not null)
                        o.AddInterceptors(interceptor);
                });
                services.AddSingleton<ITenantContext, NoTenantContext>();
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
                services.AddSingleton<ISuppressionListRepository>(new FakeSuppressionListRepository());
                services.AddSingleton<IEmailProviderRateLimiter>(new FakeEmailProviderRateLimiter());
                services.AddSingleton(sender);
                services.AddSingleton<IConnectedMailboxSender>(new FakeConnectedMailboxSender());
                services.AddSingleton<IInlineAssetFetcher>(new FakeInlineAssetFetcher());
                services.AddSingleton<IOutboundAttachmentFetcher>(new FakeOutboundAttachmentFetcher());
                services.AddSingleton<ITenantDirectoryRepository>(new FakeTenantDirectoryRepository());
                services.AddSingleton<ICorrelationContext>(new FakeCorrelationContext());
            })
            .UseWolverine(options =>
            {
                var discovery = options.Discovery.DisableConventionalDiscovery();
                foreach (var type in HandlerTypes)
                    discovery.IncludeType(type);
                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options.Durability.Mode = DurabilityMode.Solo;
                options.PersistMessagesWithSqlServer(_connectionString, Schema);
                options.Policies.UseDurableOutboxOnAllSendingEndpoints();
                options
                    .UseEntityFrameworkCoreTransactions()
                    .WithDbContextAbstraction<IUnitOfWork, PostmasterDbContext>();
                options.Policies.AutoApplyTransactions();
                options.ApplyStandardFailurePolicies();
                // Igual que Program.cs.
                options.LocalQueueFor<DispatchSentMessageCommand>().UseDurableInbox();
            })
            .Build();
        await host.StartAsync();
        return host;
    }

    private async Task<int> CountAsync(string table)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM [{table}] WHERE TenantId = @tenant";
        command.Parameters.AddWithValue("@tenant", _tenantId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<(string Status, DateTime? AttemptedAt, string? ErrorReason)> ReadSentMessageAsync()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Status, SendAttemptedAtUtc, ErrorReason FROM SentMessages WHERE TenantId = @tenant";
        command.Parameters.AddWithValue("@tenant", _tenantId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetDateTime(1),
            reader.IsDBNull(2) ? null : reader.GetString(2)
        );
    }

    /// <summary>
    /// Otra conexión busca la reserva. Si la fila sigue en una transacción abierta, la lectura espera el
    /// candado (o no la ve con snapshot): en los dos casos todavía no es visible.
    /// </summary>
    private async Task<bool> ReservationIsVisibleAsync(string idempotencyKey)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SET LOCK_TIMEOUT 3000;
            SELECT COUNT(*) FROM EmailIdempotency WHERE TenantId = @tenant AND IdempotencyKey = @key;
            """;
        command.Parameters.AddWithValue("@tenant", _tenantId);
        command.Parameters.AddWithValue("@key", idempotencyKey);
        try
        {
            return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
        }
        catch (SqlException ex) when (ex.Number == 1222)
        {
            return false;
        }
    }

    // Borra solo lo que Wolverine creó en el esquema de la prueba.
    private async Task DropWolverineSchemaAsync()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @sql nvarchar(max) = N'';
            SELECT @sql += N'ALTER TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name)
                + N' DROP CONSTRAINT ' + QUOTENAME(fk.name) + N';'
              FROM sys.foreign_keys fk JOIN sys.tables t ON t.object_id = fk.parent_object_id
              JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE s.name = @schema;
            SELECT @sql += N'DROP PROCEDURE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(o.name) + N';'
              FROM sys.procedures o JOIN sys.schemas s ON s.schema_id = o.schema_id WHERE s.name = @schema;
            SELECT @sql += N'DROP FUNCTION ' + QUOTENAME(s.name) + N'.' + QUOTENAME(o.name) + N';'
              FROM sys.objects o JOIN sys.schemas s ON s.schema_id = o.schema_id
              WHERE s.name = @schema AND o.type IN ('FN', 'IF', 'TF');
            SELECT @sql += N'DROP TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(o.name) + N';'
              FROM sys.tables o JOIN sys.schemas s ON s.schema_id = o.schema_id WHERE s.name = @schema;
            SELECT @sql += N'DROP SEQUENCE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(o.name) + N';'
              FROM sys.sequences o JOIN sys.schemas s ON s.schema_id = o.schema_id WHERE s.name = @schema;
            SELECT @sql += N'DROP TYPE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N';'
              FROM sys.types t JOIN sys.schemas s ON s.schema_id = t.schema_id
              WHERE s.name = @schema AND t.is_user_defined = 1;
            IF SCHEMA_ID(@schema) IS NOT NULL SELECT @sql += N'DROP SCHEMA ' + QUOTENAME(@schema) + N';';
            EXEC sp_executesql @sql;
            """;
        command.Parameters.AddWithValue("@schema", Schema);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    /// <summary>Acepta siempre y cuenta las llamadas.</summary>
    private class CountingEmailSender : IEmailSender
    {
        private int _sendCount;
        public int SendCount => _sendCount;

        public virtual Task<SendResult> SendAsync(
            SentMessage message,
            RenderedContent content,
            ResolvedEmailProvider provider,
            IReadOnlyList<InlineAssetBytes> inlineAssets,
            IReadOnlyList<OutboundAttachmentBytes> attachments,
            CancellationToken ct
        )
        {
            Interlocked.Increment(ref _sendCount);
            return Task.FromResult(new SendResult(true, $"it-{Guid.NewGuid():N}", null, []));
        }
    }

    /// <summary>Avisa cuando lo llaman y espera a que lo liberen antes de aceptar.</summary>
    private sealed class GateEmailSender : CountingEmailSender
    {
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _released.TrySetResult();

        public override async Task<SendResult> SendAsync(
            SentMessage message,
            RenderedContent content,
            ResolvedEmailProvider provider,
            IReadOnlyList<InlineAssetBytes> inlineAssets,
            IReadOnlyList<OutboundAttachmentBytes> attachments,
            CancellationToken ct
        )
        {
            Entered.TrySetResult();
            await _released.Task.WaitAsync(TimeSpan.FromSeconds(60), ct);
            return await base.SendAsync(message, content, provider, inlineAssets, attachments, ct);
        }
    }

    /// <summary>Hace fallar una sola vez la primera confirmación posterior al envío.</summary>
    private sealed class FailFirstSaveAfterSend(Func<bool> sent) : SaveChangesInterceptor
    {
        private int _fired;
        public bool Fired => _fired == 1;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        ) =>
            sent() && Interlocked.CompareExchange(ref _fired, 1, 0) == 0
                ? throw new InvalidOperationException("Simulated commit failure.")
                : base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
