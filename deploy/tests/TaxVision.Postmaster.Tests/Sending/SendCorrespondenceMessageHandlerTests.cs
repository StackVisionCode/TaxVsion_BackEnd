using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Application.Common;
using TaxVision.Postmaster.Application.Providers;
using TaxVision.Postmaster.Application.Sending;
using TaxVision.Postmaster.Application.Sending.Commands.RecordSendResult;
using TaxVision.Postmaster.Application.Sending.Commands.SendCorrespondenceMessage;
using TaxVision.Postmaster.Domain.Providers;
using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Tests.Consumers;

namespace TaxVision.Postmaster.Tests.Sending;

public sealed class SendCorrespondenceMessageHandlerTests
{
    private sealed record Fixture(
        FakeConnectedMailboxResolver ProviderResolver,
        FakeSuppressionListRepository SuppressionList,
        FakeOutboundAttachmentFetcher AttachmentFetcher,
        FakeConnectedMailboxSender EmailSender,
        FakeSentMessageRepository SentMessages,
        FakeIdempotencyGuard IdempotencyGuard,
        FakeUnitOfWork UnitOfWork
    );

    private static Fixture CreateFixture()
    {
        var providerResolver = new FakeConnectedMailboxResolver
        {
            ResolveReturnValue = new MailboxResolveResult(
                MailboxResolutionStatus.Resolved,
                new ResolvedMailbox(Guid.NewGuid(), "gmail", "office@tenant.example", "Front Office"),
                null
            ),
        };
        return new Fixture(
            providerResolver,
            new FakeSuppressionListRepository(),
            new FakeOutboundAttachmentFetcher(),
            new FakeConnectedMailboxSender(),
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
        SendCorrespondenceMessageCommand command,
        Action<object>? beforeInvoke = null,
        ILogger? logger = null
    )
    {
        var bus = new FakeMessageBus { BeforeInvoke = beforeInvoke };
        EmailSendPipeline.RouteSendCommands(
            bus,
            fixture.IdempotencyGuard,
            fixture.SentMessages,
            fixture.UnitOfWork,
            fixture.ProviderResolver,
            fixture.SuppressionList,
            logger
        );
        return SendCorrespondenceMessageHandler.Handle(
            command,
            fixture.AttachmentFetcher,
            fixture.EmailSender,
            bus,
            new RecordSendResultRetryOptions(),
            CancellationToken.None
        );
    }

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
        fixture.ProviderResolver.ResolveReturnValue = new MailboxResolveResult(
            MailboxResolutionStatus.ProviderNotConfigured,
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

    // PMS-10: sin el Message-ID en el replay, el borrador que cierra el job no enlaza la respuesta del cliente (COR-16).
    [Fact]
    public async Task Replay_returns_the_internet_message_id_of_the_send()
    {
        var fixture = CreateFixture();
        fixture.IdempotencyGuard.Stateful = true;
        fixture.EmailSender.SendReturnValue = new SendResult(true, "gmail-msg-1", null, [])
        {
            InternetMessageId = "<sent-1@mail.test>",
        };
        var command = ValidCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await HandleAsync(fixture, command);
        var replay = await HandleAsync(fixture, command);

        Assert.True(replay.IsSuccess);
        Assert.False(replay.Value.InProgress);
        Assert.Equal("sent-1@mail.test", replay.Value.InternetMessageId);
        Assert.Equal(1, fixture.EmailSender.SendCount);
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

    // Dos envíos del mismo draft a la vez: quien pierde al marcar el intento responde "en curso" y no envía.
    [Fact]
    public async Task Conflict_marking_the_attempt_answers_in_progress_without_sending()
    {
        var fixture = CreateFixture();
        var command = ValidCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var result = await HandleAsync(
            fixture,
            command,
            message =>
            {
                if (
                    message
                    is TaxVision.Postmaster.Application.Sending.Commands.MarkSendAttempted.MarkSendAttemptedCommand
                )
                    throw new ConflictException("Persistence.ConcurrencyConflict", "Modified.");
            }
        );

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.InProgress);
        Assert.Equal(0, fixture.EmailSender.SendCount);
    }

    /// <summary>
    /// El buzón aceptó el correo y la primera confirmación posterior falla; el mismo draft se reintenta en
    /// línea. La reserva tiene que haber quedado confirmada antes del envío para que no salga dos veces.
    /// </summary>
    [Fact]
    public async Task Retry_after_mailbox_accepted_does_not_resend()
    {
        var fixture = CreateFixture();
        fixture.IdempotencyGuard.Stateful = true;
        fixture.UnitOfWork.FailOnceWhen = () => fixture.EmailSender.SendCount > 0;
        var command = ValidCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await Assert.ThrowsAnyAsync<Exception>(() => InvokeLikeWolverineAsync(fixture, command));
        await InvokeLikeWolverineAsync(fixture, command);

        Assert.Equal(1, fixture.EmailSender.SendCount);
    }

    /// <summary>
    /// Como Wolverine: un handler sin <c>[NonTransactional]</c> corre en una transacción y, si lanza, se
    /// revierte lo que confirmó (acá, las reservas del guard).
    /// </summary>
    private static async Task<Result<SendCorrespondenceMessageResult>> InvokeLikeWolverineAsync(
        Fixture fixture,
        SendCorrespondenceMessageCommand command
    )
    {
        var transactional =
            typeof(SendCorrespondenceMessageHandler)
                .GetMethod(nameof(SendCorrespondenceMessageHandler.Handle))!
                .GetCustomAttributes(typeof(Wolverine.Attributes.NonTransactionalAttribute), false)
                .Length == 0;
        var snapshot = fixture.IdempotencyGuard.Snapshot();
        try
        {
            return await HandleAsync(fixture, command);
        }
        catch when (transactional)
        {
            fixture.IdempotencyGuard.Restore(snapshot);
            throw;
        }
    }

    [Fact]
    public async Task Handle_WithAttachmentFetchFailure_RollsBackAttemptSoTheSameDraftCanRetry()
    {
        var fixture = CreateFixture();
        fixture.AttachmentFetcher.FetchReturnValue = Result.Failure<IReadOnlyList<OutboundAttachmentBytes>>(
            new Error("OutboundAttachmentFetcher.Download", "presigned download failed.")
        );
        var command = ValidCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var result = await HandleAsync(fixture, command);

        Assert.True(result.IsFailure);
        Assert.Equal("SendCorrespondenceMessageHandler.AttachmentFetchFailed", result.Error.Code);
        Assert.Null(fixture.EmailSender.LastMessage);
        // Rollback: nada salió al proveedor, así que el intento se deshace por completo — se libera la
        // reserva (NO se completa: completarla replicaba el fallo como falso éxito) y se borra el
        // SentMessage encolado (el índice único por draft lo bloquearía en el reintento).
        Assert.Single(fixture.IdempotencyGuard.Released);
        Assert.Empty(fixture.IdempotencyGuard.Completed);
        Assert.Single(fixture.SentMessages.Removed);
        Assert.Empty(fixture.SentMessages.Added);
    }

    // COR-19: sin esto, el mensaje se guarda sin destinatarios y llega al envío.
    [Theory]
    [InlineData("no-es-un-correo")]
    [InlineData("customer@example.com", "no-es-un-correo")]
    public async Task Invalid_to_fails_and_leaves_nothing_saved(params string[] to)
    {
        var fixture = CreateFixture();
        var command = ValidCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()) with { To = to };

        var result = await HandleAsync(fixture, command);

        Assert.True(result.IsFailure);
        Assert.Equal("SendCorrespondenceMessageHandler.InvalidRecipient", result.Error.Code);
        Assert.Contains("no-es-un-correo", result.Error.Message);
        Assert.Empty(fixture.SentMessages.Added);
        Assert.Equal(0, fixture.IdempotencyGuard.ReserveCount);
        Assert.Equal(0, fixture.UnitOfWork.SaveChangesCallCount);
        Assert.Equal(0, fixture.EmailSender.SendCount);
    }

    [Fact]
    public async Task No_to_fails_and_leaves_nothing_saved()
    {
        var fixture = CreateFixture();
        var command = ValidCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()) with { To = [] };

        var result = await HandleAsync(fixture, command);

        Assert.True(result.IsFailure);
        Assert.Equal("SendCorrespondenceMessageHandler.InvalidRecipient", result.Error.Code);
        Assert.Empty(fixture.SentMessages.Added);
        Assert.Equal(0, fixture.EmailSender.SendCount);
    }

    // Igual que Postmaster en PMS-10: el correo sale al To y queda el aviso en el log.
    [Fact]
    public async Task Invalid_cc_and_bcc_are_dropped_with_a_warning_and_the_mail_goes_out()
    {
        var fixture = CreateFixture();
        var logger = new RecordingLogger();
        var command = ValidCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()) with
        {
            Cc = ["no-es-un-correo", "copy@example.com"],
            Bcc = ["tampoco"],
        };

        var result = await HandleAsync(fixture, command, logger: logger);

        Assert.True(result.IsSuccess);
        var message = Assert.Single(fixture.SentMessages.Added);
        Assert.Equal(
            ["customer@example.com", "copy@example.com"],
            message.Recipients.Select(recipient => recipient.Address)
        );
        Assert.Equal(1, fixture.EmailSender.SendCount);
        var warning = Assert.Single(logger.Warnings);
        Assert.Contains("2", warning);
        Assert.DoesNotContain("no-es-un-correo", warning);
    }

    [Fact]
    public async Task A_repeated_to_still_sends_once_to_that_address()
    {
        var fixture = CreateFixture();
        var command = ValidCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()) with
        {
            To = ["customer@example.com", "Customer@Example.com"],
        };

        var result = await HandleAsync(fixture, command);

        Assert.True(result.IsSuccess);
        Assert.Single(Assert.Single(fixture.SentMessages.Added).Recipients);
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }

    [Fact]
    public async Task Handle_MalformedCommand_ThrowsArgumentExceptionWithoutPersisting()
    {
        // Con la regla general, InvokeAsync lo reintentaría en línea 21 s dentro del pedido HTTP.
        var fixture = CreateFixture();
        var command = ValidCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()) with { Subject = " " };

        var thrown = await Record.ExceptionAsync(() => HandleAsync(fixture, command));

        Assert.IsAssignableFrom<ArgumentException>(thrown);
        Assert.Empty(fixture.SentMessages.Added);
        Assert.Null(fixture.EmailSender.LastMessage);
    }
}
