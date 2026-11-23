using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine;
using TaxVision.DataCollection.Engine.Answers;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Commands;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using Wolverine;
using Kinds = BuildingBlocks.Messaging.ClientFormsIntegrationEvents.FormParticipantNotificationKinds;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Commands;

public static class ReturnFormRequestHandler
{
    public static async Task<Result<FormRequestResponse>> Handle(
        ReturnFormRequestCommand command,
        IFormRequestRepository requests,
        IFormRequestVisibilityFilter visibility,
        IFormTemplateVersionRepository versions,
        IFormEngine engine,
        IAnswersProtector protector,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        IClientFormsMetrics metrics,
        IFormParticipantRepository participants,
        IFormAccessGrantRepository grants,
        IFormExternalAccessSecurity security,
        IProtectedPayloadCodec codec,
        CancellationToken ct
    )
    {
        var found = await LoadVisibleAsync(command, requests, visibility, ct);
        if (found.IsFailure)
            return Result.Failure<FormRequestResponse>(found.Error);
        var valid = await ValidateFeedbackAsync(command, found.Value, versions, engine, protector, ct);
        if (valid.IsFailure)
            return Result.Failure<FormRequestResponse>(valid.Error);
        var returned = ReturnForChanges(found.Value, command);
        // Externo sin vincular: el aviso de devolución sale de aquí, con un enlace nuevo emitido en la misma transacción.
        var notice = returned.IsSuccess
            ? await PrepareExternalNoticeAsync(found.Value, command, participants, grants, security, ct)
            : null;
        var saved = await ReviewSupport.SaveReviewed(
            returned,
            found.Value,
            "request.returned",
            ClientFormsMetricValues.Returned,
            command.ByUserId,
            command.NowUtc,
            audit,
            metrics,
            unitOfWork,
            ct
        );
        // Guardar antes de publicar (T-8): un conflicto no deja salir el aviso de una devolución revertida.
        if (saved.IsSuccess)
            await PublishAsync(found.Value, command.ByUserId, notice, codec, bus, correlation);
        return saved;
    }

    private static async Task PublishAsync(
        FormRequest request,
        Guid byUserId,
        ExternalNotice? notice,
        IProtectedPayloadCodec codec,
        IMessageBus bus,
        ICorrelationContext correlation
    )
    {
        var fact = await PublishReturnedAsync(request, byUserId, bus, correlation);
        if (notice is not null)
            await bus.PublishAsync(NoticeCommand(fact, notice, codec, correlation));
    }

    private sealed record ExternalNotice(FormParticipant Participant, string? AccessToken, DateTime? ExpiresAtUtc);

    // Con cliente (directo o vinculado) el aviso ya sale del hecho en Notification.
    private static async Task<ExternalNotice?> PrepareExternalNoticeAsync(
        FormRequest request,
        ReturnFormRequestCommand command,
        IFormParticipantRepository participants,
        IFormAccessGrantRepository grants,
        IFormExternalAccessSecurity security,
        CancellationToken ct
    )
    {
        if (
            request.Participant.Kind != FormParticipantKind.External
            || request.Participant.CustomerId is not null
            || request.Participant.ParticipantId is not { } participantId
        )
            return null;
        var participant = await participants.GetAsync(request.TenantId, participantId, ct);
        if (participant is null)
            return null;
        if (request.Origin != FormRequestOrigin.Directed)
            return new ExternalNotice(participant, null, null);

        var grant = await grants.GetByRequestAsync(request.TenantId, request.Id, ct);
        if (grant is null)
            return new ExternalNotice(participant, null, null);
        var token = security.GenerateToken();
        var expiresAtUtc = command.NowUtc.Add(ResendExternalFormAccessHandler.GrantLifetime);
        var reissued = grant.ReissueForReturn(token.TokenHash, token.TokenLast4, expiresAtUtc, command.NowUtc);
        return reissued.IsSuccess
            ? new ExternalNotice(participant, token.RawToken, expiresAtUtc)
            : new ExternalNotice(participant, null, null);
    }

    // El id del hecho fija el del comando: una reentrega pide el mismo aviso y Notification lo deduplica.
    private static FormParticipantNotificationRequestedIntegrationEvent NoticeCommand(
        FormRequestReturnedIntegrationEvent fact,
        ExternalNotice notice,
        IProtectedPayloadCodec codec,
        ICorrelationContext correlation
    ) =>
        new()
        {
            EventId = fact.EventId,
            CommandId = fact.EventId,
            TenantId = fact.TenantId,
            CorrelationId = correlation.CorrelationId,
            FormRequestId = fact.FormRequestId,
            Participant = fact.Participant,
            Kind = Kinds.Returned,
            Payload = codec.Protect(
                ClientFormProtectedPayloadSchemas.ParticipantNotification,
                new ProtectedPayloadBinding(fact.TenantId, fact.FormRequestId, fact.Participant),
                new FormParticipantNotificationPayload(
                    notice.Participant.Email,
                    notice.Participant.FullName,
                    fact.Title,
                    new Dictionary<string, string> { ["comment_count"] = fact.FieldCommentCount.ToString() },
                    notice.Participant.Language,
                    notice.AccessToken,
                    notice.ExpiresAtUtc
                )
            ),
        };

    private static Task<Result<FormRequest>> LoadVisibleAsync(
        ReturnFormRequestCommand command,
        IFormRequestRepository requests,
        IFormRequestVisibilityFilter visibility,
        CancellationToken ct
    ) =>
        ReviewSupport.LoadVisible(
            command.TenantId,
            command.ByUserId,
            command.CanViewAll,
            command.RequestId,
            requests,
            visibility,
            ct
        );

    // RowVersion vigente, textos sin identificadores y cada comentario sobre un campo de la versión fijada.
    private static async Task<Result> ValidateFeedbackAsync(
        ReturnFormRequestCommand command,
        FormRequest request,
        IFormTemplateVersionRepository versions,
        IFormEngine engine,
        IAnswersProtector protector,
        CancellationToken ct
    )
    {
        var safe = ReviewSupport.EnsureWritable(
            request,
            command.RowVersion,
            [command.Message, .. command.FieldComments.Select(item => item.Comment)]
        );
        if (safe.IsFailure || command.FieldComments.Count == 0)
            return safe;
        var revision = request.Submission?.Revisions.FirstOrDefault(item =>
            item.RevisionNumber == request.CurrentRevisionNumber
        );
        if (revision is null)
            return Result.Success();
        var version = await versions.GetByIdAsync(request.TenantId, request.FormTemplateVersionId, ct);
        var definition = version.IsFailure ? null : engine.ParseDefinition(version.Value.Definition.Json);
        var answers = engine.ParseAnswers(protector.Unprotect(revision.Answers));
        if (definition is null || definition.IsFailure || answers.IsFailure)
            return Result.Failure(FormRequestErrors.InvalidComponent);
        return command.FieldComments.All(item => TargetsField(item, definition.Value, answers.Value))
            ? Result.Success()
            : Result.Failure(FormRequestErrors.InvalidComponent);
    }

    // En un repeater el comentario apunta a una fila que existe en esa revisión; fuera de él, sin fila.
    private static bool TargetsField(FormFieldCommentInput comment, FormDefinition definition, FormAnswers answers)
    {
        var component = FormRequestApplicationSupport.FindComponent(definition, comment.Address.ComponentId);
        if (component is null)
            return false;
        if (!component.Value.Repeated)
            return comment.Address.RowId is null;
        return comment.Address.RowId is { } rowId
            && FormRequestApplicationSupport.HasRow(answers, definition, comment.Address.ComponentId, rowId);
    }

    private static Result ReturnForChanges(FormRequest request, ReturnFormRequestCommand command) =>
        request.ReturnForChanges(command.Message, command.FieldComments, command.ByUserId, command.NowUtc);

    private static async Task<FormRequestReturnedIntegrationEvent> PublishReturnedAsync(
        FormRequest request,
        Guid byUserId,
        IMessageBus bus,
        ICorrelationContext correlation
    )
    {
        var revision = request.Submission!.Revisions.Single(item =>
            item.RevisionNumber == request.CurrentRevisionNumber
        );
        var fact = FormRequestIntegrationEventFactory.Returned(request, revision, byUserId, correlation.CorrelationId);
        await bus.PublishAsync(fact);
        return fact;
    }
}
