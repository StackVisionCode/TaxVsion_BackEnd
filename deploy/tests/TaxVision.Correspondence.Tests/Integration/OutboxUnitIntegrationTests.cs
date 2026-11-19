using System.Collections.Concurrent;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CorrespondenceIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using JasperFx;
using JasperFx.CodeGeneration.Model;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Compose;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Infrastructure.Persistence;
using TaxVision.Correspondence.Infrastructure.Persistence.Repositories;
using TaxVision.Correspondence.Tests.Compose;
using TaxVision.Correspondence.Tests.Projections;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;

namespace TaxVision.Correspondence.Tests.Integration;

/// <summary>
/// El aviso de correo enviado sale por el outbox real, junto con el borrador ya guardado como Sent: desde el envío
/// (handler sin transacción propia) y desde la reconciliación (job). Base temporal propia.
/// </summary>
public sealed class OutboxUnitIntegrationTests : IAsyncLifetime
{
    private const string Prefix = "corsent_";

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly FakePostmasterClient _postmaster = new();
    private readonly string _connectionString = new SqlConnectionStringBuilder(CorrespondenceSqlServer.ConnectionString)
    {
        InitialCatalog = $"{Prefix}{Guid.NewGuid():N}"[..(Prefix.Length + 12)],
    }.ConnectionString;
    private IHost _host = null!;

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }

    private CorrespondenceDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<CorrespondenceDbContext>().UseSqlServer(_connectionString).Options,
            new FixedTenant(_tenantId)
        );

    public async Task InitializeAsync()
    {
        await DropLeftoversAsync();
        await using (var db = NewContext())
            await db.Database.EnsureCreatedAsync();

        _host = await Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options.Discovery.DisableConventionalDiscovery();
                options.Discovery.IncludeType(typeof(SendDraftHandler));
                options.Discovery.IncludeType(typeof(EmailSentProbeHandler));
                options.Durability.Mode = DurabilityMode.Solo;
                options.PersistMessagesWithSqlServer(_connectionString);
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options
                    .UseEntityFrameworkCoreTransactions()
                    .WithDbContextAbstraction<IUnitOfWork, CorrespondenceDbContext>();
                options.Policies.AutoApplyTransactions();
                options.PublishMessage<CorrespondenceEmailSentIntegrationEvent>().ToLocalQueue("corsent-probe");
                var services = options.Services;
                services.AddSingleton<ITenantContext>(new FixedTenant(_tenantId));
                services.AddDbContext<CorrespondenceDbContext>(db => db.UseSqlServer(_connectionString));
                services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CorrespondenceDbContext>());
                services.AddScoped<IDraftRepository, DraftRepository>();
                services.AddScoped<ICorrespondenceAuditLogRepository, CorrespondenceAuditLogRepository>();
                services.AddScoped<IOutboxUnit, OutboxUnit>();
                services.AddScoped<ICorrelationContext, FakeCorrelationContext>();
                services.AddSingleton<IPostmasterClient>(_postmaster);
            })
            .StartAsync();
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
    public async Task Send_SavesSentAndPublishesThroughTheOutbox()
    {
        var actorId = Guid.NewGuid();
        var draft = await SeedAsync(sending: false);

        var result = await _host
            .Services.GetRequiredService<IMessageBus>()
            .InvokeAsync<BuildingBlocks.Results.Result<SendDraftResult>>(
                new SendDraftCommand(_tenantId, draft.Id, actorId, "corr-sent")
            );

        Assert.True(result.IsSuccess);
        var sent = await WaitForEmailSentAsync(draft.Id);
        Assert.Equal(actorId, sent.SentByUserId);
        Assert.Null(sent.ThreadId);
        Assert.Null(sent.CustomerId);
        await using var db = NewContext();
        Assert.Equal(DraftStatus.Sent, (await db.Drafts.AsNoTracking().SingleAsync(d => d.Id == draft.Id)).Status);
    }

    [Fact]
    public async Task Reconcile_SavesSentAndPublishesOnce()
    {
        var draft = await SeedAsync(sending: true);

        await ReconcileAsync(draft.Id);
        await ReconcileAsync(draft.Id);

        var sent = await WaitForEmailSentAsync(draft.Id);
        Assert.Equal(draft.CreatedByUserId, sent.SentByUserId);
        await Task.Delay(500);
        Assert.Equal(1, EmailSentProbeHandler.Count(draft.Id));
        await using var db = NewContext();
        Assert.Equal(DraftStatus.Sent, (await db.Drafts.AsNoTracking().SingleAsync(d => d.Id == draft.Id)).Status);
    }

    private async Task ReconcileAsync(Guid draftId)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        await new DraftSendReconciler(
            scope.ServiceProvider.GetRequiredService<IDraftRepository>(),
            _postmaster,
            scope.ServiceProvider.GetRequiredService<ICorrespondenceAuditLogRepository>(),
            scope.ServiceProvider.GetRequiredService<IOutboxUnit>(),
            Options.Create(new DraftSendReconciliationOptions()),
            NullLogger<DraftSendReconciler>.Instance
        ).ReconcileAsync(_tenantId, draftId);
    }

    private async Task<Draft> SeedAsync(bool sending)
    {
        var draft = Draft.CreateNew(_tenantId, null, Guid.NewGuid(), Guid.NewGuid()).Value;
        draft.AutoSave(
            "Subject",
            "<p>body</p>",
            null,
            [new DraftRecipientData(EmailAddress.Create("to@corsent.test").Value, EmailRecipientType.To, null)]
        );
        if (sending)
            draft.MarkSending();
        await using var seed = NewContext();
        seed.Drafts.Add(draft);
        await seed.SaveChangesAsync();
        return draft;
    }

    private static async Task<CorrespondenceEmailSentIntegrationEvent> WaitForEmailSentAsync(Guid draftId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (EmailSentProbeHandler.Received.TryGetValue(draftId, out var received))
                return received.First();
            await Task.Delay(100);
        }
        throw new TimeoutException("The email sent event never arrived.");
    }

    // Solo las de más de una hora: las recientes pueden ser de otra prueba corriendo ahora.
    private async Task DropLeftoversAsync()
    {
        var master = new SqlConnectionStringBuilder(_connectionString) { InitialCatalog = "master" }.ConnectionString;
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();

        var leftovers = new List<string>();
        await using (var find = connection.CreateCommand())
        {
            find.CommandText =
                $"SELECT name FROM sys.databases WHERE name LIKE '{Prefix.Replace("_", "[_]")}%' "
                + "AND create_date < DATEADD(HOUR, -1, GETDATE())";
            await using var reader = await find.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                leftovers.Add(reader.GetString(0));
        }

        foreach (var name in leftovers)
        {
            await using var drop = connection.CreateCommand();
            drop.CommandText =
                $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];";
            await drop.ExecuteNonQueryAsync();
        }
    }
}

/// <summary>Lo que de verdad salió por el outbox, por borrador.</summary>
public static class EmailSentProbeHandler
{
    public static ConcurrentDictionary<
        Guid,
        ConcurrentQueue<CorrespondenceEmailSentIntegrationEvent>
    > Received { get; } = new();

    public static int Count(Guid draftId) => Received.TryGetValue(draftId, out var all) ? all.Count : 0;

    public static void Handle(CorrespondenceEmailSentIntegrationEvent message) =>
        Received.GetOrAdd(message.DraftId, _ => new()).Enqueue(message);
}
