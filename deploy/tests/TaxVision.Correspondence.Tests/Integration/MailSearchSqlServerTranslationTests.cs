using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Search;
using TaxVision.Correspondence.Infrastructure.Persistence;
using TaxVision.Correspondence.Infrastructure.Persistence.Repositories;
using Xunit;

namespace TaxVision.Correspondence.Tests.Integration;

/// <summary>
/// InMemory no traduce a SQL: esto ejecuta la búsqueda y los listados con <c>q</c> contra un SQL Server real
/// (solo lectura, tenant aleatorio → 0 filas) para atrapar consultas que SQL Server no sabe traducir. Opt-in:
/// sin <c>CORRESPONDENCE_SQLSERVER_TESTS</c> (connection string) no hace nada.
/// </summary>
public sealed class MailSearchSqlServerTranslationTests
{
    private sealed class TenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId { get; private set; } = tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Search_and_folder_queries_translate_on_sql_server(bool restricted, bool gated)
    {
        var connectionString = Environment.GetEnvironmentVariable("CORRESPONDENCE_SQLSERVER_TESTS");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        var tenantId = Guid.NewGuid();
        await using var db = new CorrespondenceDbContext(
            new DbContextOptionsBuilder<CorrespondenceDbContext>().UseSqlServer(connectionString).Options,
            new TenantContext(tenantId)
        );
        Guid? restrictTo = restricted ? Guid.NewGuid() : null;
        IReadOnlyCollection<Guid>? visible = gated ? [Guid.NewGuid()] : null;
        // Con y sin los filtros opcionales, para que SQL Server traduzca cada rama.
        var criteria = new MailSearchCriteria(
            tenantId,
            "inv%_[x]",
            10,
            visible,
            restrictTo,
            restrictTo,
            restricted ? Guid.NewGuid() : null,
            Unlinked: !restricted,
            HasAttachmentsOnly: gated,
            UnreadOnly: gated,
            FromUtc: DateTime.UtcNow.AddDays(-30),
            ToUtc: DateTime.UtcNow
        );

        var search = new MailSearchRepository(db);
        Assert.Empty(await search.SearchEmailsAsync(criteria));
        Assert.Empty(await search.SearchFilesAsync(criteria));
        Assert.Empty(await search.SearchPeopleAsync(criteria));

        // Campos, carpeta y personas en cada carpeta (con y sin remitentes resueltos / buzones propios).
        MailSearchFolder?[] folders = [null, .. Enum.GetValues<MailSearchFolder>()];
        foreach (var folder in folders)
        {
            var fields = criteria with
            {
                UnreadOnly = false,
                From = "ana",
                To = "bob",
                Subject = "w-2",
                Folder = folder,
                SenderAccountIds = gated ? [Guid.NewGuid()] : [],
                OwnMailboxAddresses = gated ? ["office@firm.com"] : [],
            };
            Assert.Empty(await search.SearchEmailsAsync(fields));
            Assert.Empty(await search.SearchEmailsAsync(fields with { Term = null }));
            Assert.Empty(await search.SearchFilesAsync(fields with { Term = null }));
            Assert.Empty(await search.SearchPeopleAsync(fields));
            Assert.Empty(await search.SearchPeopleAsync(fields with { From = null, To = null }));
        }

        var scope = DraftCustomerScope.All.RestrictedTo(restrictTo);
        var drafts = new DraftRepository(db);
        Assert.Empty(
            (
                await drafts.ListSentByCustomerAsync(
                    tenantId,
                    scope,
                    1,
                    20,
                    visible,
                    new MailListFilter("maria", true, DateTime.UtcNow.AddDays(-30), DateTime.UtcNow)
                )
            ).Items
        );
        Assert.Empty(
            (
                await drafts.ListOpenByCustomerAsync(
                    tenantId,
                    scope,
                    1,
                    20,
                    restrictTo,
                    new MailListFilter("maria", true, DateTime.UtcNow.AddDays(-30), DateTime.UtcNow)
                )
            ).Items
        );
        Assert.Empty(
            (
                await drafts.ListTrashedSentByCustomerAsync(
                    tenantId,
                    scope,
                    1,
                    20,
                    visible,
                    new MailListFilter("maria", true, DateTime.UtcNow.AddDays(-30), DateTime.UtcNow)
                )
            ).Items
        );
        Assert.Empty(
            (
                await new IncomingEmailRepository(db).ListTrashedByCustomerAsync(
                    tenantId,
                    null,
                    restrictTo,
                    1,
                    20,
                    visible,
                    new MailListFilter("maria", true, DateTime.UtcNow.AddDays(-30), DateTime.UtcNow)
                )
            ).Items
        );

        // R2: no leídos con avisos de entrega, Inbox sin cliente y último aviso por hilo.
        var threads = new EmailThreadRepository(db);
        var ids = new[] { Guid.NewGuid() };
        Assert.Empty(
            (
                await threads.ListVisibleAsync(
                    new InboxThreadFilter(
                        tenantId,
                        null,
                        null,
                        true,
                        "x",
                        1,
                        20,
                        visible,
                        restrictTo,
                        restricted,
                        true,
                        DateTime.UtcNow.AddDays(-30),
                        DateTime.UtcNow
                    )
                )
            ).Items
        );
        Assert.Equal(0, (await threads.CountUnreadAsync(tenantId, visible, restrictTo)).Threads);
        Assert.Empty(
            await new IncomingEmailRepository(db).GetThreadIdsWithAttachmentsAsync(tenantId, [Guid.NewGuid()])
        );
        Assert.Empty(await new IncomingEmailRepository(db).GetUnreadCountsByThreadAsync(tenantId, ids));
        var notices = new DeliveryNoticeRepository(db);
        Assert.Empty(await notices.GetLatestByThreadAsync(tenantId, ids));
        Assert.False(
            await notices.ExistsAsync(
                tenantId,
                Guid.NewGuid(),
                "a@b.com",
                TaxVision.Correspondence.Domain.Delivery.DeliveryOutcome.Delayed
            )
        );
    }

    [Fact]
    public async Task A_bounce_round_trips_on_sql_server_inside_a_rolled_back_transaction()
    {
        var connectionString = Environment.GetEnvironmentVariable("CORRESPONDENCE_SQLSERVER_TESTS");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        var tenantId = Guid.NewGuid();
        var account = Guid.NewGuid();
        await using var db = new CorrespondenceDbContext(
            new DbContextOptionsBuilder<CorrespondenceDbContext>().UseSqlServer(connectionString).Options,
            new TenantContext(tenantId)
        );
        await using var tx = await db.Database.BeginTransactionAsync();

        var sent = Search.MailSearchTestData.Sent(
            tenantId,
            null,
            account,
            "Quote",
            "maria@x.com",
            null,
            Search.MailSearchTestData.File("quote.pdf")
        );
        db.Drafts.Add(sent);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await TaxVision.Correspondence.Application.Delivery.PostmasterCorrespondenceDeliveryFeedbackConsumer.Handle(
            new BuildingBlocks.Messaging.EmailIntegrationEvents.PostmasterCorrespondenceDeliveryFeedbackIntegrationEvent
            {
                TenantId = tenantId,
                SentMessageId = sent.SentMessageId!.Value,
                CorrespondenceDraftId = sent.Id,
                Recipient = "maria@x.com",
                Outcome = "NotDelivered",
                Category = "InvalidAddress",
                Suppressed = true,
                EventAtUtc = DateTime.UtcNow,
            },
            new DraftRepository(db),
            new EmailThreadRepository(db),
            new DeliveryNoticeRepository(db),
            db,
            new Ingest.FakeMessageBus(),
            new Projections.FakeCorrelationContext(),
            Microsoft
                .Extensions
                .Logging
                .Abstractions
                .NullLogger<TaxVision.Correspondence.Domain.Delivery.DeliveryNotice>
                .Instance,
            CancellationToken.None
        );
        db.ChangeTracker.Clear();

        var page = await new EmailThreadRepository(db).ListVisibleAsync(
            new InboxThreadFilter(tenantId, null, null, true, "Quote", 1, 20, [account], Guid.NewGuid())
        );
        Assert.Equal(sent.Id, Assert.Single(page.Items).Id);
        Assert.Single(
            await new MailSearchRepository(db).SearchFilesAsync(
                new MailSearchCriteria(tenantId, "quote", 10, [account], null, null)
            )
        );

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task People_aggregate_on_sql_server_inside_a_rolled_back_transaction()
    {
        var connectionString = Environment.GetEnvironmentVariable("CORRESPONDENCE_SQLSERVER_TESTS");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        var tenantId = Guid.NewGuid();
        var account = Guid.NewGuid();
        var customer = Guid.NewGuid();
        await using var db = new CorrespondenceDbContext(
            new DbContextOptionsBuilder<CorrespondenceDbContext>().UseSqlServer(connectionString).Options,
            new TenantContext(tenantId)
        );
        await using var tx = await db.Database.BeginTransactionAsync();

        var thread = Search.MailSearchTestData.Thread(tenantId, customer, "Docs", DateTime.UtcNow.AddDays(-3));
        var incoming = Search.MailSearchTestData.Incoming(
            tenantId,
            thread,
            account,
            from: "ana@client.com",
            fromName: "Ana Perez",
            at: DateTime.UtcNow.AddDays(-3),
            to: ["office@firm.com"]
        );
        var sent = Search.MailSearchTestData.Outgoing(
            tenantId,
            null,
            account,
            "Docs",
            true,
            null,
            ("ana@client.com", null, TaxVision.Correspondence.Domain.Inbox.EmailRecipientType.To),
            ("office@firm.com", "Office", TaxVision.Correspondence.Domain.Inbox.EmailRecipientType.Cc)
        );
        db.AddRange(thread, incoming, sent);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var people = await new MailSearchRepository(db).SearchPeopleAsync(
            new MailSearchCriteria(
                tenantId,
                "ana",
                10,
                [account],
                null,
                null,
                Subject: "Docs",
                OwnMailboxAddresses: ["office@firm.com"]
            )
        );

        var person = Assert.Single(people);
        Assert.Equal("ana@client.com", person.Address);
        Assert.Equal(2, person.MessageCount);
        Assert.Equal(sent.UpdatedAtUtc, person.LastContactAtUtc, TimeSpan.FromMilliseconds(1));
        // El enviado no trae nombre ni cliente: ganan los del entrante.
        Assert.Equal("Ana Perez", person.DisplayName);
        Assert.Equal(customer, person.CustomerId);
        Assert.Equal("Both", person.Direction);

        await tx.RollbackAsync();
    }
}
