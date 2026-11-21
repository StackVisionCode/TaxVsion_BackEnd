using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Correspondence.Application.Backfill;
using TaxVision.Correspondence.Application.Files;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Tests.Compose;
using TaxVision.Correspondence.Tests.Ingest;
using TaxVision.Correspondence.Tests.Projections;
using static TaxVision.Correspondence.Tests.Search.MailSearchTestData;

namespace TaxVision.Correspondence.Tests.Files;

/// <summary>
/// CloudStorage pregunta de quién son archivos sin origen: Correspondence declara solo los adjuntos suyos de esa
/// oficina (enviados y entrantes ya descargados) con los mismos valores que la reposición manual.
/// </summary>
public sealed class FileSourcesClaimConsumerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _otherTenant = Guid.NewGuid();
    private readonly Guid _account = Guid.NewGuid();
    private readonly Guid _customer = Guid.NewGuid();
    private readonly FakeDraftRepository _drafts = new();
    private readonly FakeIncomingEmailRepository _incoming = new();

    private Draft _sentWithCustomer = null!;
    private Draft _sentWithoutCustomer = null!;
    private Draft _openDraft = null!;
    private Draft _foreignSent = null!;
    private IncomingEmail _downloaded = null!;
    private IncomingEmail _downloadedWithoutCustomer = null!;
    private IncomingEmail _foreignIncoming = null!;

    [Fact]
    public async Task Declares_sent_and_incoming_files_with_the_same_values_as_the_reconciliation()
    {
        await SeedAsync();
        var bus = new FakeMessageBus();

        await HandleAsync(bus, _tenantId, AllFiles());

        Assert.Equal((await ReconciledAsync()).Order(), Keys(bus.Published).Order());
        Assert.Equal(5, bus.Published.Count);
    }

    [Fact]
    public async Task Declares_only_the_claimed_ids_and_ignores_foreign_ones()
    {
        await SeedAsync();
        var bus = new FakeMessageBus();
        var sentFile = _sentWithCustomer.Attachments.First().FileId;
        var incomingFile = Stored(_downloaded).Single();

        await HandleAsync(
            bus,
            _tenantId,
            [
                sentFile,
                incomingFile,
                _openDraft.Attachments.Single().FileId,
                Stored(_foreignIncoming).Single(),
                Guid.NewGuid(),
            ]
        );

        Assert.Equal(
            new[]
            {
                Key(_tenantId, "email_message", _sentWithCustomer.Id, sentFile, "outgoing_attachment", _customer)
                    + "|correspondence",
                Key(_tenantId, "incoming_email", _downloaded.Id, incomingFile, "incoming_attachment", _customer)
                    + "|correspondence",
            }.Order(),
            Keys(bus.Published).Order()
        );
    }

    [Fact]
    public async Task Another_office_file_is_declared_only_for_its_own_tenant()
    {
        await SeedAsync();
        var ours = new FakeMessageBus();
        var theirs = new FakeMessageBus();
        Guid[] foreign = [_foreignSent.Attachments.Single().FileId, Stored(_foreignIncoming).Single()];

        await HandleAsync(ours, _tenantId, foreign);
        await HandleAsync(theirs, _otherTenant, foreign);

        Assert.Empty(ours.Published);
        Assert.Equal(
            foreign.Order(),
            theirs.Published.Cast<FileSourceDeclaredIntegrationEvent>().Select(e => e.FileId).Order()
        );
    }

    [Fact]
    public async Task Without_matches_nothing_is_published()
    {
        await SeedAsync();
        var bus = new FakeMessageBus();

        await HandleAsync(bus, _tenantId, [_openDraft.Attachments.Single().FileId, Guid.NewGuid()]);

        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task An_empty_list_or_an_empty_tenant_does_nothing()
    {
        await SeedAsync();
        var bus = new FakeMessageBus();

        await HandleAsync(bus, _tenantId, []);
        await HandleAsync(bus, Guid.Empty, AllFiles());

        Assert.Empty(bus.Published);
    }

    private Task HandleAsync(FakeMessageBus bus, Guid tenantId, IReadOnlyList<Guid> fileIds) =>
        FileSourcesClaimConsumer.Handle(
            new FileSourcesClaimRequestedIntegrationEvent { TenantId = tenantId, FileIds = fileIds },
            _drafts,
            _incoming,
            bus,
            CancellationToken.None
        );

    // Lo que publica la reposición manual de la oficina.
    private async Task<List<string>> ReconciledAsync()
    {
        var bus = new FakeMessageBus();
        await RedeclareFileSourcesHandler.Handle(
            new RedeclareFileSourcesCommand(_tenantId, DryRun: false),
            _drafts,
            _incoming,
            new FakeOutboxUnit(new FakeUnitOfWork(), bus),
            NullLogger<RedeclareFileSourcesCommand>.Instance,
            CancellationToken.None
        );
        return Keys(bus.Published).ToList();
    }

    private Guid[] AllFiles() =>
        [.. _drafts.All.SelectMany(d => d.Attachments.Select(a => a.FileId)), .. _incoming.All.SelectMany(Stored)];

    private static IEnumerable<Guid> Stored(IncomingEmail email) =>
        email.Attachments.Where(a => a.CloudStorageFileId is not null).Select(a => a.CloudStorageFileId!.Value);

    private static IEnumerable<string> Keys(IEnumerable<object> messages) =>
        messages
            .Cast<FileSourceDeclaredIntegrationEvent>()
            .Select(e =>
                Key(e.TenantId, e.SourceResourceType, e.SourceResourceId, e.FileId, e.SourceRole, e.RelatedCustomerId)
                + $"|{e.SourceService}"
            );

    private static string Key(Guid tenant, string type, Guid resourceId, Guid fileId, string role, Guid? customerId) =>
        $"{tenant}|{type}|{resourceId}|{fileId}|{role}|{customerId}";

    private async Task SeedAsync()
    {
        _sentWithCustomer = Sent(
            _tenantId,
            _customer,
            _account,
            "Return",
            "ana@client.com",
            null,
            File("a.pdf"),
            File("b.pdf")
        );
        _sentWithoutCustomer = Sent(_tenantId, null, _account, "Other", "ana@client.com", null, File("c.pdf"));
        _openDraft = Open(_tenantId, _customer, _account, "Still a draft", "ana@client.com", null, File("d.pdf"));
        _foreignSent = Sent(_otherTenant, null, _account, "Foreign", "a@b.com", null, File("e.pdf"));
        foreach (var draft in new[] { _sentWithCustomer, _sentWithoutCustomer, _openDraft, _foreignSent })
            await _drafts.AddAsync(draft);

        _downloaded = MailFileTestData.Incoming(
            MailFileTestData.Thread(_tenantId, _customer),
            _account,
            Guid.NewGuid()
        );
        _downloadedWithoutCustomer = MailFileTestData.Incoming(
            MailFileTestData.Thread(_tenantId, null),
            _account,
            Guid.NewGuid()
        );
        var notOpened = MailFileTestData.Incoming(MailFileTestData.Thread(_tenantId, _customer), _account, null);
        _foreignIncoming = MailFileTestData.Incoming(
            MailFileTestData.Thread(_otherTenant, null),
            _account,
            Guid.NewGuid()
        );
        foreach (var email in new[] { _downloaded, _downloadedWithoutCustomer, notOpened, _foreignIncoming })
            await _incoming.AddAsync(email);
    }
}
