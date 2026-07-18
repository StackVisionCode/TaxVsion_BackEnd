using BuildingBlocks.Results;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Application.Common;
using TaxVision.Postmaster.Application.Providers;
using TaxVision.Postmaster.Application.Sending;
using TaxVision.Postmaster.Application.Sending.Commands.SendCorrespondenceMessage;
using TaxVision.Postmaster.Domain.Providers;
using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Tests.Consumers;

namespace TaxVision.Postmaster.Tests.Sending;

public sealed class SendCorrespondenceMessageHandlerTests
{
    private sealed record Fixture(
        FakeOAuthProviderResolver ProviderResolver,
        FakeSuppressionListRepository SuppressionList,
        FakeOutboundAttachmentFetcher AttachmentFetcher,
        FakeOAuthEmailSender EmailSender,
        FakeSentMessageRepository SentMessages,
        FakeIdempotencyGuard IdempotencyGuard,
        FakeUnitOfWork UnitOfWork
    );

    private static Fixture CreateFixture()
    {
        var providerResolver = new FakeOAuthProviderResolver
        {
            ResolveReturnValue = new OAuthResolveResult(
                OAuthResolutionStatus.Resolved,
                new ResolvedOAuthProvider(Guid.NewGuid(), "gmail", "office@tenant.example", "Front Office"),
                null
            ),
        };
        return new Fixture(
            providerResolver,
            new FakeSuppressionListRepository(),
            new FakeOutboundAttachmentFetcher(),
            new FakeOAuthEmailSender(),
            new FakeSentMessageRepository(),
            new FakeIdempotencyGuard(),
            new FakeUnitOfWork()
        );
    }

    private static SendCorrespondenceMessageCommand ValidCommand(Guid tenantId, Guid draftId, Guid accountId) =>
        new(
            tenantId,
            draftId,
            accountId,
            "Tax question follow-up",
            "<p>Hi</p>",
            "Hi",
            ["customer@example.com"],
            [],
            [],
            [],
            InReplyToInternetMessageId: null,
            References: null,
            ReplyToProviderMessageId: null
        );

    private static Task<Result<SendCorrespondenceMessageResult>> HandleAsync(
        Fixture fixture,
        SendCorrespondenceMessageCommand command
    ) =>
        SendCorrespondenceMessageHandler.Handle(
            command,
            fixture.ProviderResolver,
            fixture.SuppressionList,
            fixture.AttachmentFetcher,
            fixture.EmailSender,
            fixture.SentMessages,
            fixture.IdempotencyGuard,
            fixture.UnitOfWork,
            CancellationToken.None
        );

    [Fact]
    public async Task Handle_ValidCommand_SendsAndReturnsProviderMessageId()
    {
        var fixture = CreateFixture();
        fixture.EmailSender.SendReturnValue = new SendResult(true, "gmail-msg-1", null, []);
        var tenantId = Guid.NewGuid();
        var command = ValidCommand(tenantId, Guid.NewGuid(), Guid.NewGuid());

        var result = await HandleAsync(fixture, command);

        Assert.True(result.IsSuccess);
        Assert.Equal("gmail-msg-1", result.Value.ProviderMessageId);
        var message = Assert.Single(fixture.SentMessages.Added);
        Assert.Equal(SentMessageStatus.Sent, message.Status);
        Assert.Equal(command.CorrespondenceDraftId, message.CorrespondenceDraftId);
        Assert.Single(fixture.IdempotencyGuard.Completed);
    }

    [Fact]
    public async Task Handle_ConnectorsSendFails_MarksMessageFailedAndReturnsError()
    {
        var fixture = CreateFixture();
        fixture.EmailSender.SendReturnValue = new SendResult(false, null, "Connectors send failed (502).", []);
        var command = ValidCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var result = await HandleAsync(fixture, command);

        Assert.True(result.IsFailure);
        Assert.Equal("SendCorrespondenceMessageHandler.ConnectorsSendFailed", result.Error.Code);
        var message = Assert.Single(fixture.SentMessages.Added);
        Assert.Equal(SentMessageStatus.Failed, message.Status);
    }

    [Fact]
    public async Task Handle_AllRecipientsSuppressed_MarksMessageSuppressedWithoutSending()
    {
        var fixture = CreateFixture();
        fixture.SuppressionList.SuppressedAddresses.Add("customer@example.com");
        var command = ValidCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var result = await HandleAsync(fixture, command);

        Assert.True(result.IsFailure);
        Assert.Equal("SendCorrespondenceMessageHandler.AllRecipientsSuppressed", result.Error.Code);
        var message = Assert.Single(fixture.SentMessages.Added);
        Assert.Equal(SentMessageStatus.Suppressed, message.Status);
        Assert.Null(fixture.EmailSender.LastMessage);
    }

    [Fact]
    public async Task Handle_AccountNotResolved_FailsCleanWithoutPersistingAnything()
    {
        var fixture = CreateFixture();
        fixture.ProviderResolver.ResolveReturnValue = new OAuthResolveResult(
            OAuthResolutionStatus.ProviderNotConfigured,
            null,
            "not found"
        );
        var command = ValidCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var result = await HandleAsync(fixture, command);

        Assert.True(result.IsFailure);
        Assert.Equal("SendCorrespondenceMessageHandler.AccountNotFound", result.Error.Code);
        Assert.Empty(fixture.SentMessages.Added);
    }

    [Fact]
    public async Task Handle_SameCorrespondenceDraftId_ReplaysWithoutSendingAgain()
    {
        var fixture = CreateFixture();
        var existingSentMessageId = Guid.NewGuid();
        fixture.IdempotencyGuard.ReserveReturnValue = IdempotencyReservationResult.AlreadyCompleted(
            existingSentMessageId
        );
        var command = ValidCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var result = await HandleAsync(fixture, command);

        Assert.True(result.IsSuccess);
        Assert.Equal(existingSentMessageId, result.Value.SentMessageId);
        Assert.Null(result.Value.ProviderMessageId);
        Assert.Empty(fixture.SentMessages.Added);
        Assert.Null(fixture.EmailSender.LastMessage);
    }

    /// <summary>
    /// El bug real del plan §Fase 11: antes de este fix, InProgress se conflaba con "reserva nueva" y
    /// el handler creaba un segundo <c>SentMessage</c> para el mismo draft en una carrera real
    /// (doble-click en "Enviar"). Ahora debe devolver un 409 real (mapeado en <c>ErrorHttpMapping</c>)
    /// sin persistir nada, en vez de un 200 falso o un 500 sin manejar.
    /// </summary>
    [Fact]
    public async Task Handle_ConcurrentSendInProgress_ReturnsSendInProgressWithoutPersistingAnything()
    {
        var fixture = CreateFixture();
        fixture.IdempotencyGuard.ReserveReturnValue = IdempotencyReservationResult.InProgress();
        var command = ValidCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var result = await HandleAsync(fixture, command);

        Assert.True(result.IsFailure);
        Assert.Equal("SendCorrespondenceMessageHandler.SendInProgress", result.Error.Code);
        Assert.Empty(fixture.SentMessages.Added);
        Assert.Null(fixture.EmailSender.LastMessage);
    }

    /// <summary>
    /// Defensa-en-profundidad (plan §Fase 11, punto 4): la reserva de idempotencia se ganó
    /// (<c>Reserved</c>), pero el índice único real de <c>SentMessages</c> revienta en la ventana
    /// angosta entre dos reservas concurrentes. Antes de este fix, el <c>ConflictException</c> subía
    /// sin atrapar hasta <c>ExceptionHandlingMiddleware</c> como un 500 genérico; ahora se traduce al
    /// mismo error 409 que el race-loss del guard.
    /// </summary>
    [Fact]
    public async Task Handle_SaveChangesConflictsOnUniqueIndex_ReturnsSendInProgressInsteadOfThrowing()
    {
        var fixture = CreateFixture();
        fixture.UnitOfWork.ThrowConflictOnSaveChangesCall = 1;
        var command = ValidCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var result = await HandleAsync(fixture, command);

        Assert.True(result.IsFailure);
        Assert.Equal("SendCorrespondenceMessageHandler.SendInProgress", result.Error.Code);
        Assert.Null(fixture.EmailSender.LastMessage);
    }

    [Fact]
    public async Task Handle_WithAttachmentFetchFailure_MarksMessageFailedWithoutCallingSender()
    {
        var fixture = CreateFixture();
        fixture.AttachmentFetcher.FetchReturnValue = Result.Failure<IReadOnlyList<OutboundAttachmentBytes>>(
            new Error("OutboundAttachmentFetcher.Download", "presigned download failed.")
        );
        var command = ValidCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var result = await HandleAsync(fixture, command);

        Assert.True(result.IsFailure);
        Assert.Equal("SendCorrespondenceMessageHandler.AttachmentFetchFailed", result.Error.Code);
        var message = Assert.Single(fixture.SentMessages.Added);
        Assert.Equal(SentMessageStatus.Failed, message.Status);
        Assert.Null(fixture.EmailSender.LastMessage);
    }
}
