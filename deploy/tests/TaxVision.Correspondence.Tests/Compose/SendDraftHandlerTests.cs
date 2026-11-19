using BuildingBlocks.Messaging.CorrespondenceIntegrationEvents;
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
        FakeUnitOfWork unitOfWork,
        FakeMessageBus? bus = null
    ) =>
        await SendDraftHandler.Handle(
            command,
            drafts,
            postmaster,
            auditLogs,
            correlation,
            unitOfWork,
            new FakeOutboxUnit(unitOfWork, bus),
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
        // Vacío a propósito: al correr vía bus.InvokeAsync, el ICorrelationContext del scope de Wolverine
        // viene vacío. La correlación real llega por el command (la puso el controller).
        var correlation = new FakeCorrelationContext();
        var unitOfWork = new FakeUnitOfWork();

        var result = await InvokeAsync(
            new SendDraftCommand(tenantId, draft.Id, Guid.NewGuid(), "corr-1"),
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
        // El audit se graba con la correlación del COMMAND aunque el contexto inyectado esté vacío
        // (antes se descartaba por validación → la tabla CorrespondenceAuditLogs quedaba vacía).
        Assert.Equal("corr-1", Assert.Single(auditLogs.All).CorrelationId);
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
            new SendDraftCommand(tenantId, draft.Id, Guid.NewGuid(), "corr-1"),
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
            new SendDraftCommand(tenantId, draft.Id, Guid.NewGuid(), "corr-1"),
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

    /// <summary>Sin respuesta de Postmaster el correo pudo salir: queda en Sending para el job, sin pasar a Failed.</summary>
    [Theory]
    [InlineData("PostmasterClient.RequestFailed")]
    [InlineData("PostmasterClient.EmptyResponse")]
    [InlineData("PostmasterClient.UnexpectedStatus")]
    public async Task Handle_OnAnUnknownOutcome_StaysSendingAndAnswersInProgress(string errorCode)
    {
        var tenantId = Guid.NewGuid();
        var drafts = new FakeDraftRepository();
        var draft = SeedSendableDraft(drafts, tenantId);
        var postmaster = new FakePostmasterClient
        {
            Response = Result.Failure<SendDraftPostmasterResult>(new Error(errorCode, "The request timed out.")),
        };
        var auditLogs = new FakeCorrespondenceAuditLogRepository();
        var correlation = new FakeCorrelationContext();
        correlation.Set("corr-1");
        var unitOfWork = new FakeUnitOfWork();

        var result = await InvokeAsync(
            new SendDraftCommand(tenantId, draft.Id, Guid.NewGuid(), "corr-1"),
            drafts,
            postmaster,
            auditLogs,
            correlation,
            unitOfWork
        );

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.InProgress);
        Assert.Equal(DraftStatus.Sending, draft.Status);
        Assert.Null(draft.FailureReason);
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
            new SendDraftCommand(tenantId, draft.Id, Guid.NewGuid(), "corr-1"),
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
            new SendDraftCommand(tenantId, draft.Id, Guid.NewGuid(), "corr-1"),
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
            new SendDraftCommand(tenantId, draft.Id, Guid.NewGuid(), "corr-1"),
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
            new SendDraftCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "corr-1"),
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

    private static Draft SeedSendableReply(FakeDraftRepository drafts, Guid tenantId, Guid customerId, Guid threadId)
    {
        var context = ReplyContext.Create(Guid.NewGuid(), threadId, null, null, null).Value;
        var draft = Draft.CreateReply(tenantId, customerId, Guid.NewGuid(), Guid.NewGuid(), context, "Hello").Value;
        draft
            .AutoSave(
                "Re: Hello",
                "<p>body</p>",
                null,
                [new DraftRecipientData(EmailAddress.Create("customer@example.com").Value, EmailRecipientType.To, null)]
            )
            .EnsureSuccess();
        drafts.AddAsync(draft).GetAwaiter().GetResult();
        return draft;
    }

    [Fact]
    public async Task Handle_Sent_PublishesEmailSentOnce_WithActorThreadAndCustomer()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var drafts = new FakeDraftRepository();
        var draft = SeedSendableReply(drafts, tenantId, customerId, threadId);
        var bus = new FakeMessageBus();

        var result = await InvokeAsync(
            new SendDraftCommand(tenantId, draft.Id, actorId, "corr-sent"),
            drafts,
            new FakePostmasterClient(),
            new FakeCorrespondenceAuditLogRepository(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork(),
            bus
        );

        Assert.True(result.IsSuccess);
        var sent = Assert.IsType<CorrespondenceEmailSentIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal(tenantId, sent.TenantId);
        Assert.Equal(draft.Id, sent.DraftId);
        Assert.Equal(threadId, sent.ThreadId);
        Assert.Equal(customerId, sent.CustomerId);
        Assert.Equal(actorId, sent.SentByUserId);
        Assert.Equal("corr-sent", sent.CorrelationId);
    }

    [Fact]
    public async Task Handle_SentToNonCustomer_PublishesWithoutCustomer_AndTheMessageAsItsThread()
    {
        var tenantId = Guid.NewGuid();
        var drafts = new FakeDraftRepository();
        var draft = Draft.CreateNew(tenantId, null, Guid.NewGuid(), Guid.NewGuid()).Value;
        draft
            .AutoSave(
                "Subject",
                "<p>body</p>",
                null,
                [new DraftRecipientData(EmailAddress.Create("vendor@example.com").Value, EmailRecipientType.To, null)]
            )
            .EnsureSuccess();
        await drafts.AddAsync(draft);
        var bus = new FakeMessageBus();

        await InvokeAsync(
            new SendDraftCommand(tenantId, draft.Id, Guid.NewGuid(), "corr-1"),
            drafts,
            new FakePostmasterClient(),
            new FakeCorrespondenceAuditLogRepository(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork(),
            bus
        );

        var sent = Assert.IsType<CorrespondenceEmailSentIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Null(sent.CustomerId);
        Assert.Null(sent.ThreadId);
    }

    [Fact]
    public async Task Handle_SendRetried_PublishesOnlyOnce()
    {
        var tenantId = Guid.NewGuid();
        var drafts = new FakeDraftRepository();
        var draft = SeedSendableDraft(drafts, tenantId);
        var bus = new FakeMessageBus();
        var command = new SendDraftCommand(tenantId, draft.Id, Guid.NewGuid(), "corr-1");

        await InvokeAsync(command, drafts, new FakePostmasterClient(), new(), new(), new(), bus);
        var again = await InvokeAsync(command, drafts, new FakePostmasterClient(), new(), new(), new(), bus);

        Assert.True(again.IsFailure);
        Assert.Single(bus.Published);
    }

    [Fact]
    public async Task Handle_SentWithoutStaffActor_DoesNotPublish()
    {
        var tenantId = Guid.NewGuid();
        var drafts = new FakeDraftRepository();
        var draft = SeedSendableDraft(drafts, tenantId);
        var bus = new FakeMessageBus();

        var result = await InvokeAsync(
            new SendDraftCommand(tenantId, draft.Id, Guid.Empty, "corr-1"),
            drafts,
            new FakePostmasterClient(),
            new FakeCorrespondenceAuditLogRepository(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork(),
            bus
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(DraftStatus.Sent, draft.Status);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Handle_SendNotConfirmed_DoesNotPublish()
    {
        var tenantId = Guid.NewGuid();
        var drafts = new FakeDraftRepository();
        var draft = SeedSendableDraft(drafts, tenantId);
        var bus = new FakeMessageBus();
        var postmaster = new FakePostmasterClient
        {
            Response = Result.Success(new SendDraftPostmasterResult(Guid.NewGuid(), null, InProgress: true)),
        };

        await InvokeAsync(
            new SendDraftCommand(tenantId, draft.Id, Guid.NewGuid(), "corr-1"),
            drafts,
            postmaster,
            new FakeCorrespondenceAuditLogRepository(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork(),
            bus
        );

        Assert.Equal(DraftStatus.Sending, draft.Status);
        Assert.Empty(bus.Published);
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
