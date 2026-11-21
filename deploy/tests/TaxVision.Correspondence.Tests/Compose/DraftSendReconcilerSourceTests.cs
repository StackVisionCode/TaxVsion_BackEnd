using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Compose;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Tests.Ingest;
using TaxVision.Correspondence.Tests.Projections;

namespace TaxVision.Correspondence.Tests.Compose;

// File Library: el job que cierra un envío también declara los adjuntos, solo si queda enviado.
public sealed class DraftSendReconcilerSourceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly FakeDraftRepository _drafts = new();
    private readonly FakePostmasterClient _postmaster = new();
    private readonly FakeMessageBus _bus = new();

    private Draft SeedSending(Guid fileId)
    {
        var draft = Draft.CreateNew(_tenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value;
        draft.AutoSave(
            "Subject",
            "<p>body</p>",
            null,
            [new DraftRecipientData(EmailAddress.Create("to@example.com").Value, EmailRecipientType.To, null)]
        );
        draft.AttachFile(DraftAttachmentRef.Create(fileId, "w2.pdf", "application/pdf", 10).Value);
        draft.MarkSending();
        _drafts.AddAsync(draft).GetAwaiter().GetResult();
        return draft;
    }

    private Task ReconcileAsync(Draft draft) =>
        new DraftSendReconciler(
            _drafts,
            _postmaster,
            new FakeCorrespondenceAuditLogRepository(),
            new FakeOutboxUnit(new FakeUnitOfWork(), _bus),
            Options.Create(new DraftSendReconciliationOptions { GiveUpAfter = TimeSpan.FromHours(2) }),
            NullLogger<DraftSendReconciler>.Instance
        ).ReconcileAsync(_tenantId, draft.Id);

    [Fact]
    public async Task ClosedAsSent_DeclaresTheAttachment()
    {
        var file = Guid.NewGuid();
        var draft = SeedSending(file);
        _postmaster.Response = Result.Success(new SendDraftPostmasterResult(Guid.NewGuid(), null));

        await ReconcileAsync(draft);

        var declared = Assert.Single(_bus.Published.OfType<FileSourceDeclaredIntegrationEvent>());
        Assert.Equal(file, declared.FileId);
        Assert.Equal(draft.Id, declared.SourceResourceId);
        Assert.Equal(draft.CustomerId, declared.RelatedCustomerId);
    }

    [Fact]
    public async Task ClosedAsFailed_DeclaresNothing()
    {
        var draft = SeedSending(Guid.NewGuid());
        _postmaster.Response = Result.Failure<SendDraftPostmasterResult>(
            new Error("SendCorrespondenceMessageHandler.AllRecipientsSuppressed", "suppressed")
        );

        await ReconcileAsync(draft);

        Assert.Equal(DraftStatus.Failed, draft.Status);
        Assert.Empty(_bus.Published.OfType<FileSourceDeclaredIntegrationEvent>());
    }
}
