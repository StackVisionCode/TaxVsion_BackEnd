using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Compose;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Tests.Projections;

namespace TaxVision.Correspondence.Tests.Compose;

public sealed class SendDraftHandlerTests
{
    private static Draft SeedSendableDraft(FakeDraftRepository drafts, Guid tenantId)
    {
        var draft = Draft.CreateNew(tenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value;
        draft
            .AutoSave(
                "Subject",
                "<p>body</p>",
                null,
                [new DraftRecipientData(EmailAddress.Create("customer@example.com").Value, EmailRecipientType.To, null)]
            )
            .EnsureSuccess();
        drafts.AddAsync(draft).GetAwaiter().GetResult();
        return draft;
    }

    private static async Task<Result<SendDraftResult>> InvokeAsync(
        SendDraftCommand command,
        FakeDraftRepository drafts,
        FakePostmasterClient postmaster,
        FakeCorrespondenceAuditLogRepository auditLogs,
        FakeCorrelationContext correlation,
        FakeUnitOfWork unitOfWork
    ) =>
        await SendDraftHandler.Handle(
            command,
            drafts,
            postmaster,
            auditLogs,
            correlation,
            unitOfWork,
            NullLogger<SendDraftCommand>.Instance,
            CancellationToken.None
        );

    [Fact]
    public async Task Handle_HappyPath_MarksSendingThenSent_AndReturnsPostmasterIds()
    {
        var tenantId = Guid.NewGuid();
        var drafts = new FakeDraftRepository();
        var draft = SeedSendableDraft(drafts, tenantId);
        var sentMessageId = Guid.NewGuid();
        var postmaster = new FakePostmasterClient
        {
            Response = Result.Success(new SendDraftPostmasterResult(sentMessageId, "provider-abc")),
        };
        var auditLogs = new FakeCorrespondenceAuditLogRepository();
        var correlation = new FakeCorrelationContext();
        correlation.Set("corr-1");
        var unitOfWork = new FakeUnitOfWork();

        var result = await InvokeAsync(
            new SendDraftCommand(tenantId, draft.Id, Guid.NewGuid()),
            drafts,
            postmaster,
            auditLogs,
            correlation,
            unitOfWork
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(sentMessageId, result.Value.SentMessageId);
        Assert.Equal("provider-abc", result.Value.ProviderMessageId);
        Assert.Equal(DraftStatus.Sent, draft.Status);
        Assert.Equal(sentMessageId, draft.SentMessageId);
        Assert.Equal(1, postmaster.CallCount);
        Assert.Single(auditLogs.All);
        Assert.Equal(2, unitOfWork.SaveChangesCallCount); // MarkSending persist + MarkSent persist.
    }

    [Fact]
    public async Task Handle_SplitsRecipientsByType_BeforeCallingPostmaster()
    {
        var tenantId = Guid.NewGuid();
        var draft = Draft.CreateNew(tenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value;
        draft
            .AutoSave(
                "Subject",
                "<p>body</p>",
                null,
                [
                    new DraftRecipientData(EmailAddress.Create("to@example.com").Value, EmailRecipientType.To, null),
                    new DraftRecipientData(EmailAddress.Create("cc@example.com").Value, EmailRecipientType.Cc, null),
                    new DraftRecipientData(EmailAddress.Create("bcc@example.com").Value, EmailRecipientType.Bcc, null),
                ]
            )
            .EnsureSuccess();
        var drafts = new FakeDraftRepository();
        await drafts.AddAsync(draft);
        var postmaster = new FakePostmasterClient();
        var auditLogs = new FakeCorrespondenceAuditLogRepository();
        var correlation = new FakeCorrelationContext();
        correlation.Set("corr-1");
        var unitOfWork = new FakeUnitOfWork();

        await InvokeAsync(
            new SendDraftCommand(tenantId, draft.Id, Guid.NewGuid()),
            drafts,
            postmaster,
            auditLogs,
            correlation,
            unitOfWork
        );

        Assert.Equal(["to@example.com"], postmaster.LastTo);
        Assert.Equal(["cc@example.com"], postmaster.LastCc);
        Assert.Equal(["bcc@example.com"], postmaster.LastBcc);
    }

    /// <summary>
    /// HTTP status per code is verified by <c>ErrorHttpMapping</c> itself (BuildingBlocks.Web, out
    /// of this project's reference graph) — 403/409/502/502 respectively for these four codes,
    /// already mapped there since Postmaster's own Fase 5. What this test verifies is that
    /// <see cref="SendDraftHandler"/> propagates Postmaster's error CODE unchanged (so that mapping
    /// applies at all) and marks the draft Failed with the real reason.
    /// </summary>
    [Theory]
    [InlineData("SendCorrespondenceMessageHandler.AccountNotFound")]
    [InlineData("SendCorrespondenceMessageHandler.AllRecipientsSuppressed")]
    [InlineData("SendCorrespondenceMessageHandler.ConnectorsSendFailed")]
    [InlineData("SendCorrespondenceMessageHandler.AttachmentFetchFailed")]
    public async Task Handle_OnAPostmasterFailure_MarksDraftFailed_AndPropagatesTheRealErrorCode(string errorCode)
    {
        var tenantId = Guid.NewGuid();
        var drafts = new FakeDraftRepository();
        var draft = SeedSendableDraft(drafts, tenantId);
        var postmaster = new FakePostmasterClient
        {
            Response = Result.Failure<SendDraftPostmasterResult>(new Error(errorCode, "Postmaster rejected the send.")),
        };
        var auditLogs = new FakeCorrespondenceAuditLogRepository();
        var correlation = new FakeCorrelationContext();
        correlation.Set("corr-1");
        var unitOfWork = new FakeUnitOfWork();

        var result = await InvokeAsync(
            new SendDraftCommand(tenantId, draft.Id, Guid.NewGuid()),
            drafts,
            postmaster,
            auditLogs,
            correlation,
            unitOfWork
        );

        Assert.True(result.IsFailure);
        Assert.Equal(errorCode, result.Error.Code);
        Assert.Equal(DraftStatus.Failed, draft.Status);
        Assert.NotNull(draft.FailureReason);
        Assert.Contains(errorCode, draft.FailureReason);
        Assert.Single(auditLogs.All);
        Assert.Equal(2, unitOfWork.SaveChangesCallCount); // MarkSending persist + MarkFailed persist.
    }

    /// <summary>A transport-level failure from the client (timeout/network exception, already caught and turned into a Result by PostmasterClient itself) is handled exactly like any other Postmaster failure — no unhandled exception bubbles up.</summary>
    [Fact]
    public async Task Handle_OnAClientTransportFailure_MarksDraftFailed_WithoutThrowing()
    {
        var tenantId = Guid.NewGuid();
        var drafts = new FakeDraftRepository();
        var draft = SeedSendableDraft(drafts, tenantId);
        var postmaster = new FakePostmasterClient
        {
            Response = Result.Failure<SendDraftPostmasterResult>(
                new Error("PostmasterClient.RequestFailed", "The request timed out.")
            ),
        };
        var auditLogs = new FakeCorrespondenceAuditLogRepository();
        var correlation = new FakeCorrelationContext();
        correlation.Set("corr-1");
        var unitOfWork = new FakeUnitOfWork();

        var result = await InvokeAsync(
            new SendDraftCommand(tenantId, draft.Id, Guid.NewGuid()),
            drafts,
            postmaster,
            auditLogs,
            correlation,
            unitOfWork
        );

        Assert.True(result.IsFailure);
        Assert.Equal("PostmasterClient.RequestFailed", result.Error.Code);
        Assert.Equal(DraftStatus.Failed, draft.Status);
        Assert.NotNull(draft.FailureReason);
    }

    [Fact]
    public async Task Handle_OnAnAlreadySentDraft_ReturnsConflict_AndNeverCallsPostmaster()
    {
        var tenantId = Guid.NewGuid();
        var drafts = new FakeDraftRepository();
        var draft = SeedSendableDraft(drafts, tenantId);
        draft.MarkSending();
        draft.MarkSent(Guid.NewGuid());
        var postmaster = new FakePostmasterClient();
        var auditLogs = new FakeCorrespondenceAuditLogRepository();
        var correlation = new FakeCorrelationContext();
        correlation.Set("corr-1");
        var unitOfWork = new FakeUnitOfWork();

        var result = await InvokeAsync(
            new SendDraftCommand(tenantId, draft.Id, Guid.NewGuid()),
            drafts,
            postmaster,
            auditLogs,
            correlation,
            unitOfWork
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Draft.InvalidTransition", result.Error.Code);
        Assert.Equal(0, postmaster.CallCount);
        Assert.Empty(auditLogs.All);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_OnADraftMissingSubject_ReturnsBadRequest_AndNeverCallsPostmaster()
    {
        var tenantId = Guid.NewGuid();
        var draft = Draft.CreateNew(tenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value;
        draft
            .AutoSave(
                null,
                "<p>body</p>",
                null,
                [new DraftRecipientData(EmailAddress.Create("customer@example.com").Value, EmailRecipientType.To, null)]
            )
            .EnsureSuccess();
        var drafts = new FakeDraftRepository();
        await drafts.AddAsync(draft);
        var postmaster = new FakePostmasterClient();
        var auditLogs = new FakeCorrespondenceAuditLogRepository();
        var correlation = new FakeCorrelationContext();
        var unitOfWork = new FakeUnitOfWork();

        var result = await InvokeAsync(
            new SendDraftCommand(tenantId, draft.Id, Guid.NewGuid()),
            drafts,
            postmaster,
            auditLogs,
            correlation,
            unitOfWork
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SendDraftHandler.MissingRequiredFields", result.Error.Code);
        Assert.Equal(0, postmaster.CallCount);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_OnADraftWithNoRecipients_ReturnsBadRequest_AndNeverCallsPostmaster()
    {
        var tenantId = Guid.NewGuid();
        var draft = Draft.CreateNew(tenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value;
        draft.AutoSave("Subject", "<p>body</p>", null, null).EnsureSuccess();
        var drafts = new FakeDraftRepository();
        await drafts.AddAsync(draft);
        var postmaster = new FakePostmasterClient();
        var auditLogs = new FakeCorrespondenceAuditLogRepository();
        var correlation = new FakeCorrelationContext();
        var unitOfWork = new FakeUnitOfWork();

        var result = await InvokeAsync(
            new SendDraftCommand(tenantId, draft.Id, Guid.NewGuid()),
            drafts,
            postmaster,
            auditLogs,
            correlation,
            unitOfWork
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SendDraftHandler.MissingRequiredFields", result.Error.Code);
        Assert.Equal(0, postmaster.CallCount);
    }

    [Fact]
    public async Task Handle_WithUnknownDraft_ReturnsNotFound()
    {
        var drafts = new FakeDraftRepository();
        var postmaster = new FakePostmasterClient();
        var auditLogs = new FakeCorrespondenceAuditLogRepository();
        var correlation = new FakeCorrelationContext();
        var unitOfWork = new FakeUnitOfWork();

        var result = await InvokeAsync(
            new SendDraftCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            drafts,
            postmaster,
            auditLogs,
            correlation,
            unitOfWork
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Draft.NotFound", result.Error.Code);
        Assert.Equal(0, postmaster.CallCount);
    }
}

file static class ResultExtensions
{
    public static void EnsureSuccess(this Result result)
    {
        if (result.IsFailure)
            throw new InvalidOperationException(
                $"Expected success but got {result.Error.Code}: {result.Error.Message}"
            );
    }
}
