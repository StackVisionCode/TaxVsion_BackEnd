using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Compose;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Tests.Ingest;
using TaxVision.Correspondence.Tests.Projections;

namespace TaxVision.Correspondence.Tests.Compose;

// File Library: un envío confirmado declara cada adjunto como email_message/outgoing_attachment.
public sealed class SendDraftAttachmentSourcesTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly FakeDraftRepository _drafts = new();
    private readonly FakeMessageBus _bus = new();

    private Draft Seed(Guid? customerId, params Guid[] fileIds)
    {
        var draft = Draft.CreateNew(_tenantId, customerId, Guid.NewGuid(), Guid.NewGuid()).Value;
        draft.AutoSave(
            "Subject",
            "<p>body</p>",
            null,
            [new DraftRecipientData(EmailAddress.Create("to@example.com").Value, EmailRecipientType.To, null)]
        );
        foreach (var fileId in fileIds)
            draft.AttachFile(DraftAttachmentRef.Create(fileId, $"{fileId:N}.pdf", "application/pdf", 10).Value);
        _drafts.AddAsync(draft).GetAwaiter().GetResult();
        return draft;
    }

    private Task<Result<SendDraftResult>> SendAsync(Draft draft, Guid actorId, FakePostmasterClient? postmaster = null)
    {
        var unitOfWork = new FakeUnitOfWork();
        return SendDraftHandler.Handle(
            new SendDraftCommand(_tenantId, draft.Id, actorId, "corr-files"),
            _drafts,
            postmaster ?? new FakePostmasterClient(),
            new FakeCorrespondenceAuditLogRepository(),
            new FakeCorrelationContext(),
            unitOfWork,
            new FakeOutboxUnit(unitOfWork, _bus),
            NullLogger<SendDraftCommand>.Instance,
            CancellationToken.None
        );
    }

    private IReadOnlyList<FileSourceDeclaredIntegrationEvent> Declared =>
        _bus.Published.OfType<FileSourceDeclaredIntegrationEvent>().ToList();

    [Fact]
    public async Task ConfirmedSend_DeclaresEachAttachment_WithTheDraftAndItsCustomer()
    {
        var customerId = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var draft = Seed(customerId, first, second);

        var result = await SendAsync(draft, Guid.NewGuid());

        Assert.True(result.IsSuccess);
        Assert.Equivalent(new[] { first, second }, Declared.Select(e => e.FileId));
        Assert.All(
            Declared,
            e =>
            {
                Assert.Equal(_tenantId, e.TenantId);
                Assert.Equal("correspondence", e.SourceService);
                Assert.Equal("email_message", e.SourceResourceType);
                Assert.Equal(draft.Id, e.SourceResourceId);
                Assert.Equal("outgoing_attachment", e.SourceRole);
                Assert.Equal(customerId, e.RelatedCustomerId);
                Assert.Equal("corr-files", e.CorrelationId);
            }
        );
    }

    [Fact]
    public async Task ConfirmedSend_WithoutCustomer_StillDeclares()
    {
        var draft = Seed(null, Guid.NewGuid());

        await SendAsync(draft, Guid.NewGuid());

        Assert.Null(Assert.Single(Declared).RelatedCustomerId);
    }

    [Fact]
    public async Task ConfirmedSend_WithoutAttachments_DeclaresNothing()
    {
        var draft = Seed(Guid.NewGuid());

        await SendAsync(draft, Guid.NewGuid());

        Assert.Empty(Declared);
    }

    [Fact]
    public async Task FailedSend_DeclaresNothing()
    {
        var draft = Seed(Guid.NewGuid(), Guid.NewGuid());
        var postmaster = new FakePostmasterClient
        {
            Response = Result.Failure<SendDraftPostmasterResult>(
                new Error("SendCorrespondenceMessageHandler.ConnectorsSendFailed", "down")
            ),
        };

        await SendAsync(draft, Guid.NewGuid(), postmaster);

        Assert.Equal(DraftStatus.Failed, draft.Status);
        Assert.Empty(Declared);
    }

    [Fact]
    public async Task SendInProgress_DeclaresNothing()
    {
        var draft = Seed(Guid.NewGuid(), Guid.NewGuid());
        var postmaster = new FakePostmasterClient
        {
            Response = Result.Success(new SendDraftPostmasterResult(Guid.NewGuid(), null, InProgress: true)),
        };

        await SendAsync(draft, Guid.NewGuid(), postmaster);

        Assert.Equal(DraftStatus.Sending, draft.Status);
        Assert.Empty(Declared);
    }

    [Fact]
    public async Task ConfirmedSend_WithoutStaffActor_StillDeclares()
    {
        var file = Guid.NewGuid();
        var draft = Seed(Guid.NewGuid(), file);

        await SendAsync(draft, Guid.Empty);

        Assert.Equal(file, Assert.Single(Declared).FileId);
    }
}
