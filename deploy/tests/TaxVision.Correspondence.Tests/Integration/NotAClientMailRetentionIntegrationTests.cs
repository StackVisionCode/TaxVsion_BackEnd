using System.Collections.Concurrent;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
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
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Infrastructure.Jobs;
using TaxVision.Correspondence.Infrastructure.Persistence;
using TaxVision.Correspondence.Tests.Messages;
using TaxVision.Correspondence.Tests.Search;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;

namespace TaxVision.Correspondence.Tests.Integration;

/// <summary>
/// COR-18 con SQL real y el outbox real. Corre en una base temporal propia: el job borra correo de verdad y no
/// se apunta nunca a la base local. Se limpia al arrancar y al terminar.
/// </summary>
public sealed class NotAClientMailRetentionIntegrationTests : IAsyncLifetime
{
    private const string Prefix = "corn18_";

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly FakeCloudStorageClient _storage = new();
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

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

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
                options.Discovery.IncludeType(typeof(FileDeletionProbeHandler));
                options.Durability.Mode = DurabilityMode.Solo;
                options.PersistMessagesWithSqlServer(_connectionString);
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options
                    .UseEntityFrameworkCoreTransactions()
                    .WithDbContextAbstraction<IUnitOfWork, CorrespondenceDbContext>();
                options.Policies.AutoApplyTransactions();
                options.PublishMessage<FilesDeletionRequestedIntegrationEvent>().ToLocalQueue("cor18-deletions");
                var services = options.Services;
                // Como en producción: el job corre sin tenant y cruza todos.
                services.AddSingleton<ITenantContext>(new NoTenant());
                services.AddDbContext<CorrespondenceDbContext>(db => db.UseSqlServer(_connectionString));
                services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CorrespondenceDbContext>());
                services.AddSingleton<ICloudStorageClient>(_storage);
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
    public async Task ExpiredMail_GoesAwayWithItsCopiesAndAttachments()
    {
        var fileId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        _storage.MetadataResponse = Result.Success(
            new CloudStorageFileMetadata(fileId, "application/pdf", 10, "User", ownerId, "EmailIncoming")
        );
        var (threadId, emailId) = await SeedAsync(customerId: null, daysOld: 91, fileId: fileId);

        await RunAsync();

        await using var db = NewContext();
        Assert.Empty(await db.IncomingEmails.ToListAsync());
        Assert.Empty(await db.IncomingEmailRecipients.ToListAsync());
        Assert.Empty(await db.IncomingEmailAttachments.ToListAsync());
        Assert.Empty(await db.IncomingEmailMailboxes.ToListAsync());
        Assert.Null(await db.EmailThreads.FirstOrDefaultAsync(t => t.Id == threadId));

        var request = await WaitForDeletionRequestAsync(emailId);
        var target = Assert.Single(request.Files);
        Assert.Equal(fileId, target.FileId);
        Assert.Equal("User", target.ExpectedOwnerType);
        Assert.Equal(ownerId, target.ExpectedOwnerId);
        Assert.Equal("EmailIncoming", target.ExpectedFolderType);
        Assert.Equal(_tenantId, request.TenantId);
    }

    [Fact]
    public async Task LinkedMail_IsNeverDeleted()
    {
        var customerId = Guid.NewGuid();
        var (_, linked) = await SeedAsync(customerId, daysOld: 400);
        var (_, inClientThread) = await SeedAsync(customerId: null, daysOld: 400, threadCustomerId: customerId);

        await RunAsync();

        await using var db = NewContext();
        Assert.Equal(2, await db.IncomingEmails.CountAsync(e => e.Id == linked || e.Id == inClientThread));
    }

    [Fact]
    public async Task RecentMail_IsKept()
    {
        var (_, emailId) = await SeedAsync(customerId: null, daysOld: 89);

        await RunAsync();

        await using var db = NewContext();
        Assert.True(await db.IncomingEmails.AnyAsync(e => e.Id == emailId));
    }

    [Fact]
    public async Task ThePeriodIsConfigurable()
    {
        var (_, expired) = await SeedAsync(customerId: null, daysOld: 11);
        var (_, kept) = await SeedAsync(customerId: null, daysOld: 9);

        await RunAsync(new NotAClientMailRetentionOptions { Enabled = true, RetentionDays = 10 });

        await using var db = NewContext();
        Assert.False(await db.IncomingEmails.AnyAsync(e => e.Id == expired));
        Assert.True(await db.IncomingEmails.AnyAsync(e => e.Id == kept));
    }

    // Lo enviado no vence, y el hilo que queda sin entrantes no puede caer en el Inbox.
    [Fact]
    public async Task ThreadWithSentMail_StaysArchivedWithItsSent()
    {
        var (threadId, emailId) = await SeedAsync(customerId: null, daysOld: 91);
        await using (var seed = NewContext())
        {
            var sent = MailSearchTestData.Sent(_tenantId, null, Guid.NewGuid(), "Subject", "sender@cor18.test");
            sent.LinkToThread(threadId);
            seed.Drafts.Add(sent);
            await seed.SaveChangesAsync();
        }

        await RunAsync();

        await using var db = NewContext();
        Assert.False(await db.IncomingEmails.AnyAsync(e => e.Id == emailId));
        var kept = await db.EmailThreads.SingleAsync(t => t.Id == threadId);
        Assert.Equal(0, kept.MessageCount);
        Assert.Equal(EmailThreadStatus.Archived, kept.Status);
        Assert.Equal(1, await db.Drafts.CountAsync(d => d.EmailThreadId == threadId));
    }

    [Fact]
    public async Task ThreadWithNewerMail_KeepsIt()
    {
        var (threadId, oldId) = await SeedAsync(customerId: null, daysOld: 91);
        var newId = await AddToThreadAsync(threadId, daysOld: 2);

        await RunAsync();

        await using var db = NewContext();
        Assert.False(await db.IncomingEmails.AnyAsync(e => e.Id == oldId));
        Assert.True(await db.IncomingEmails.AnyAsync(e => e.Id == newId));
        Assert.Equal(1, (await db.EmailThreads.SingleAsync(t => t.Id == threadId)).MessageCount);
    }

    // Sin saber de quién es el archivo no se puede pedir borrarlo: el correo espera a la siguiente pasada.
    [Fact]
    public async Task StorageDown_KeepsTheMailForTheNextSweep()
    {
        _storage.MetadataResponse = Result.Failure<CloudStorageFileMetadata>(
            new Error("CloudStorageClient.RequestFailed", "down")
        );
        var (_, emailId) = await SeedAsync(customerId: null, daysOld: 91, fileId: Guid.NewGuid());

        await RunAsync();

        await using var db = NewContext();
        Assert.True(await db.IncomingEmails.AnyAsync(e => e.Id == emailId));
    }

    // Un archivo que ya está a nombre de un cliente dejó de ser del correo: se borra el correo, no el archivo.
    [Fact]
    public async Task FileMovedToACustomer_IsNotRequested()
    {
        _storage.MetadataResponse = Result.Success(
            new CloudStorageFileMetadata(
                Guid.NewGuid(),
                "application/pdf",
                10,
                "Customer",
                Guid.NewGuid(),
                "EmailIncoming"
            )
        );
        var (_, emailId) = await SeedAsync(customerId: null, daysOld: 91, fileId: Guid.NewGuid());

        await RunAsync();

        await using var db = NewContext();
        Assert.False(await db.IncomingEmails.AnyAsync(e => e.Id == emailId));
        await Task.Delay(500);
        Assert.False(FileDeletionProbeHandler.Received.ContainsKey(emailId));
    }

    // Nace apagado: sin encenderlo no se toca nada.
    [Fact]
    public async Task ByDefault_NothingIsDeleted()
    {
        var (_, emailId) = await SeedAsync(customerId: null, daysOld: 400);

        await RunAsync(new NotAClientMailRetentionOptions());

        await using var db = NewContext();
        Assert.True(await db.IncomingEmails.AnyAsync(e => e.Id == emailId));
    }

    private Task RunAsync(NotAClientMailRetentionOptions? options = null) =>
        new NotAClientMailRetentionJob(
            _host.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options ?? new NotAClientMailRetentionOptions { Enabled = true }),
            NullLogger<NotAClientMailRetentionJob>.Instance
        ).RunOnceAsync(CancellationToken.None);

    private async Task<(Guid ThreadId, Guid EmailId)> SeedAsync(
        Guid? customerId,
        int daysOld,
        Guid? fileId = null,
        Guid? threadCustomerId = null
    )
    {
        var at = DateTime.UtcNow.AddDays(-daysOld);
        var thread = EmailThread
            .NewFromMessage(_tenantId, threadCustomerId ?? customerId, "Subject", $"pt-{Guid.NewGuid():N}", at)
            .Value;
        var email = NewEmail(customerId, thread.Id, at, withAttachment: fileId is not null);
        email.AddMailboxCopy(Guid.NewGuid(), $"copy-{Guid.NewGuid():N}");
        if (fileId is { } id)
        {
            var attachment = email.Attachments.Single();
            attachment.MarkInProgress();
            attachment.MarkDownloaded(id);
        }

        await using var seed = NewContext();
        seed.EmailThreads.Add(thread);
        seed.IncomingEmails.Add(email);
        await seed.SaveChangesAsync();
        return (thread.Id, email.Id);
    }

    private async Task<Guid> AddToThreadAsync(Guid threadId, int daysOld)
    {
        var at = DateTime.UtcNow.AddDays(-daysOld);
        await using var seed = NewContext();
        var thread = await seed.EmailThreads.SingleAsync(t => t.Id == threadId);
        var email = NewEmail(null, threadId, at, withAttachment: false);
        thread.AppendMessage(at);
        seed.IncomingEmails.Add(email);
        await seed.SaveChangesAsync();
        return email.Id;
    }

    private IncomingEmail NewEmail(Guid? customerId, Guid threadId, DateTime at, bool withAttachment) =>
        IncomingEmail
            .Create(
                _tenantId,
                customerId,
                threadId,
                Guid.NewGuid(),
                "gmail",
                $"pm-{Guid.NewGuid():N}",
                EmailAddress.Create("sender@cor18.test").Value,
                null,
                "Subject",
                "",
                at,
                withAttachment,
                withAttachment ? 1 : 0,
                recipients:
                [
                    new IncomingEmailRecipientData(
                        EmailAddress.Create("office@cor18.test").Value,
                        EmailRecipientType.To,
                        null
                    ),
                ],
                attachments: withAttachment
                    ? [new IncomingEmailAttachmentData("a.pdf", "application/pdf", 10, "att-1", false)]
                    : null
            )
            .Value;

    private static async Task<FilesDeletionRequestedIntegrationEvent> WaitForDeletionRequestAsync(Guid emailId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (FileDeletionProbeHandler.Received.TryGetValue(emailId, out var request))
                return request;
            await Task.Delay(100);
        }
        throw new TimeoutException("The file deletion request never arrived.");
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

/// <summary>Lo que de verdad salió por el outbox, por correo de origen.</summary>
public static class FileDeletionProbeHandler
{
    public static ConcurrentDictionary<Guid, FilesDeletionRequestedIntegrationEvent> Received { get; } = new();

    public static void Handle(FilesDeletionRequestedIntegrationEvent message) =>
        Received[message.SourceReferenceId] = message;
}
