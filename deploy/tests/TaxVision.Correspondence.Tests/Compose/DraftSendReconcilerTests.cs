using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Compose;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Tests.Projections;

namespace TaxVision.Correspondence.Tests.Compose;

public sealed class DraftSendReconcilerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly FakeDraftRepository _drafts = new();
    private readonly FakePostmasterClient _postmaster = new();
    private readonly FakeCorrespondenceAuditLogRepository _auditLogs = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    private Draft SeedSending()
    {
        var draft = Draft.CreateNew(_tenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value;
        draft.AutoSave(
            "Subject",
            "<p>body</p>",
            null,
            [new DraftRecipientData(EmailAddress.Create("to@example.com").Value, EmailRecipientType.To, null)]
        );
        draft.MarkSending();
        _drafts.AddAsync(draft).GetAwaiter().GetResult();
        return draft;
    }

    private Task ReconcileAsync(Draft draft, TimeSpan? giveUpAfter = null) =>
        new DraftSendReconciler(
            _drafts,
            _postmaster,
            _auditLogs,
            _unitOfWork,
            Options.Create(new DraftSendReconciliationOptions { GiveUpAfter = giveUpAfter ?? TimeSpan.FromHours(2) }),
            NullLogger<DraftSendReconciler>.Instance
        ).ReconcileAsync(_tenantId, draft.Id);

    [Fact]
    public async Task Reconcile_PostmasterConfirms_DraftEndsSent()
    {
        var draft = SeedSending();
        var sentMessageId = Guid.NewGuid();
        _postmaster.Response = Result.Success(new SendDraftPostmasterResult(sentMessageId, null));

        await ReconcileAsync(draft);

        Assert.Equal(DraftStatus.Sent, draft.Status);
        Assert.Equal(sentMessageId, draft.SentMessageId);
        Assert.Single(_auditLogs.All);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    // COR-14: el 202 dejaba el borrador en Sending sin que nada lo cerrara.
    [Fact]
    public async Task Reconcile_StillInProgress_WaitsAndThenCloses()
    {
        var draft = SeedSending();
        var sentMessageId = Guid.NewGuid();
        _postmaster.Response = Result.Success(new SendDraftPostmasterResult(sentMessageId, null, InProgress: true));

        await ReconcileAsync(draft);

        Assert.Equal(DraftStatus.Sending, draft.Status);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);

        _postmaster.Response = Result.Success(new SendDraftPostmasterResult(sentMessageId, null));
        await ReconcileAsync(draft);

        Assert.Equal(DraftStatus.Sent, draft.Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Reconcile_NoResultPastTheDeadline_ClosesAsUnknown(bool transportFailure)
    {
        var draft = SeedSending();
        _postmaster.Response = transportFailure
            ? Result.Failure<SendDraftPostmasterResult>(new Error("PostmasterClient.RequestFailed", "Timed out."))
            : Result.Success(new SendDraftPostmasterResult(Guid.NewGuid(), null, InProgress: true));

        await ReconcileAsync(draft, giveUpAfter: TimeSpan.Zero);

        Assert.Equal(DraftStatus.Failed, draft.Status);
        Assert.StartsWith(DraftSendReconciler.OutcomeUnknownCode, draft.FailureReason);
    }

    [Fact]
    public async Task Reconcile_PostmasterRejects_DraftEndsFailed()
    {
        var draft = SeedSending();
        _postmaster.Response = Result.Failure<SendDraftPostmasterResult>(
            new Error("SendCorrespondenceMessageHandler.AllRecipientsSuppressed", "Suppressed.")
        );

        await ReconcileAsync(draft);

        Assert.Equal(DraftStatus.Failed, draft.Status);
    }

    [Fact]
    public async Task Reconcile_DraftAlreadyClosed_DoesNotCallPostmaster()
    {
        var draft = SeedSending();
        draft.MarkSent(Guid.NewGuid());

        await ReconcileAsync(draft);

        Assert.Equal(0, _postmaster.CallCount);
    }
}
