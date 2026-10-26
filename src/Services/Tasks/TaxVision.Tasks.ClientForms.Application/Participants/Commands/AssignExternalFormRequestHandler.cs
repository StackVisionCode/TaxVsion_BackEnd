using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Actions.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Consents;
using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.ClientForms.Domain.Versions;
using Wolverine;

namespace TaxVision.Tasks.ClientForms.Application.Participants.Commands;

public sealed record AssignExternalFormRequestCommand(
    Guid TenantId,
    Guid ByUserId,
    string Email,
    string? FullName,
    Guid TemplateId,
    string EntryNoticeKey,
    string? Title,
    string? MessageToClient,
    DateTime? DueAtUtc,
    int? TaxYear,
    DateTime NowUtc
);

public static class AssignExternalFormRequestHandler
{
    private static readonly TimeSpan GrantLifetime = TimeSpan.FromDays(14);

    public static async Task<Result<FormRequestResponse>> Handle(
        AssignExternalFormRequestCommand command,
        IFormParticipantRepository participants,
        IFormAccessGrantRepository grants,
        IClientFormsPublicCapabilityEvaluator capabilities,
        IFormExternalAccessSecurity security,
        IFormTemplateRepository templates,
        IFormTemplateVersionRepository versions,
        IConsentNoticeRepository notices,
        IFormRequestRepository requests,
        IFormActionAuthorizer authorizer,
        IProtectedPayloadCodec payloadCodec,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        IClientFormsMetrics metrics,
        CancellationToken ct
    )
    {
        var inputs = await LoadAndValidateAsync(command, capabilities, templates, versions, notices, authorizer, ct);
        if (inputs.IsFailure)
            return Failure(inputs.Error);
        var assignment = await PrepareAssignmentAsync(
            command,
            inputs.Value,
            participants,
            requests,
            security,
            grants,
            ct
        );
        if (assignment.IsFailure)
            return Failure(assignment.Error);
        RecordAudit(command, assignment.Value.Request.Id, audit);
        await unitOfWork.SaveChangesAsync(ct);
        await PublishAsync(command, assignment.Value, payloadCodec, bus, correlation);
        metrics.RecordRequestAssigned();
        return Result.Success(FormRequestResponse.From(assignment.Value.Request));
    }

    private sealed record PreparedAssignment(
        FormParticipant Participant,
        FormRequest Request,
        FormAccessGrant Grant,
        FormOpaqueToken Token
    );

    private static async Task<Result<PreparedAssignment>> PrepareAssignmentAsync(
        AssignExternalFormRequestCommand command,
        (ParticipantEmail Email, FormTemplateVersion Version, EntryNoticeSelection Notice) inputs,
        IFormParticipantRepository participants,
        IFormRequestRepository requests,
        IFormExternalAccessSecurity security,
        IFormAccessGrantRepository grants,
        CancellationToken ct
    )
    {
        var participant = await ResolveParticipantAsync(
            command,
            inputs.Email,
            inputs.Notice.Version.Language,
            participants,
            requests,
            ct
        );
        if (participant.IsFailure)
            return Result.Failure<PreparedAssignment>(participant.Error);
        var request = CreateRequest(command, participant.Value, inputs.Version);
        if (request.IsFailure)
            return Result.Failure<PreparedAssignment>(request.Error);
        var token = security.GenerateToken();
        var grant = CreateGrant(command, request.Value, inputs.Notice, token);
        if (grant.IsFailure)
            return Result.Failure<PreparedAssignment>(grant.Error);
        requests.Add(request.Value);
        grants.Add(grant.Value);
        return Result.Success(new PreparedAssignment(participant.Value, request.Value, grant.Value, token));
    }

    private static async Task<
        Result<(ParticipantEmail Email, FormTemplateVersion Version, EntryNoticeSelection Notice)>
    > LoadAndValidateAsync(
        AssignExternalFormRequestCommand command,
        IClientFormsPublicCapabilityEvaluator capabilities,
        IFormTemplateRepository templates,
        IFormTemplateVersionRepository versions,
        IConsentNoticeRepository notices,
        IFormActionAuthorizer authorizer,
        CancellationToken ct
    )
    {
        var email = await ValidateAsync(command, capabilities, ct);
        if (email.IsFailure)
            return Result.Failure<(ParticipantEmail, FormTemplateVersion, EntryNoticeSelection)>(email.Error);
        var version = await LoadPublishedVersionAsync(command, templates, versions, ct);
        if (version.IsFailure)
            return Result.Failure<(ParticipantEmail, FormTemplateVersion, EntryNoticeSelection)>(version.Error);
        // Quien envía queda como responsable: ejecutará las acciones de la plantilla en su nombre.
        var authorized = await authorizer.EnsureResponsibleCanRunAsync(
            command.TenantId,
            command.ByUserId,
            version.Value,
            ct
        );
        if (authorized.IsFailure)
            return Result.Failure<(ParticipantEmail, FormTemplateVersion, EntryNoticeSelection)>(authorized.Error);
        var entryNotice = await EntryNoticeSupport.ResolveForCreationAsync(
            command.TenantId,
            command.EntryNoticeKey,
            notices,
            ct
        );
        return entryNotice is null
            ? Result.Failure<(ParticipantEmail, FormTemplateVersion, EntryNoticeSelection)>(
                FormParticipantErrors.EntryNoticeRequired
            )
            : Result.Success((email.Value, version.Value, entryNotice));
    }

    private static async Task<Result<ParticipantEmail>> ValidateAsync(
        AssignExternalFormRequestCommand command,
        IClientFormsPublicCapabilityEvaluator capabilities,
        CancellationToken ct
    )
    {
        if (!await capabilities.IsExternalParticipantAvailableAsync(command.TenantId, ct))
            return Result.Failure<ParticipantEmail>(FormRequestErrors.NotFound);
        var normalizedEmail = ParticipantEmail.Create(command.Email);
        if (normalizedEmail.IsFailure)
            return normalizedEmail;
        var safeText = StaffTextGuard.Validate(command.Title, command.MessageToClient);
        return safeText.IsFailure ? Result.Failure<ParticipantEmail>(safeText.Error) : normalizedEmail;
    }

    // Un participante externo tiene a lo sumo una request abierta por plantilla.
    private static async Task<Result<FormParticipant>> ResolveParticipantAsync(
        AssignExternalFormRequestCommand command,
        ParticipantEmail email,
        string language,
        IFormParticipantRepository participants,
        IFormRequestRepository requests,
        CancellationToken ct
    )
    {
        var participant = await GetOrCreateParticipantAsync(command, email, language, participants, ct);
        if (participant.IsFailure)
            return participant;
        return await requests.HasOpenExternalRequestAsync(
            command.TenantId,
            participant.Value.Id,
            command.TemplateId,
            ct
        )
            ? Result.Failure<FormParticipant>(FormRequestErrors.AlreadyOpenForTemplate)
            : participant;
    }

    private static async Task<Result<FormTemplateVersion>> LoadPublishedVersionAsync(
        AssignExternalFormRequestCommand command,
        IFormTemplateRepository templates,
        IFormTemplateVersionRepository versions,
        CancellationToken ct
    )
    {
        var template = await templates.GetByIdAsync(command.TenantId, command.TemplateId, ct);
        if (
            template.IsFailure
            || template.Value.Status != FormTemplateStatus.Active
            || !template.Value.LatestVersionId.HasValue
        )
            return Result.Failure<FormTemplateVersion>(FormRequestErrors.TemplateNotPublished);
        var version = await versions.GetByIdAsync(command.TenantId, template.Value.LatestVersionId.Value, ct);
        return version.IsFailure
            ? Result.Failure<FormTemplateVersion>(FormRequestErrors.TemplateNotPublished)
            : version;
    }

    // Reutiliza el participante sin vincular del mismo email para no duplicar identidades externas.
    private static async Task<Result<FormParticipant>> GetOrCreateParticipantAsync(
        AssignExternalFormRequestCommand command,
        ParticipantEmail email,
        string language,
        IFormParticipantRepository participants,
        CancellationToken ct
    )
    {
        var participant = await participants.GetUnlinkedByEmailAsync(command.TenantId, email.Value, ct);
        if (participant is null)
        {
            var created = FormParticipant.Create(
                command.TenantId,
                email.Value,
                command.FullName,
                null,
                FormParticipantSource.DirectedRequest,
                command.NowUtc,
                language
            );
            if (created.IsSuccess)
                participants.Add(created.Value);
            return created;
        }

        // El idioma de un participante existente no se pisa: puede venir de su elección en la página pública.
        if (string.IsNullOrWhiteSpace(command.FullName))
            return Result.Success(participant);
        var updated = participant.UpdateContact(command.FullName, participant.PhoneE164);
        return updated.IsFailure ? Result.Failure<FormParticipant>(updated.Error) : Result.Success(participant);
    }

    private static Result<FormRequest> CreateRequest(
        AssignExternalFormRequestCommand command,
        FormParticipant participant,
        FormTemplateVersion version
    ) =>
        FormRequest.Create(
            command.TenantId,
            FormParticipantRef.External(participant.Id),
            version,
            command.Title,
            command.MessageToClient,
            command.TaxYear,
            null,
            command.DueAtUtc,
            null,
            null,
            FormRequestOrigin.Directed,
            null,
            command.ByUserId,
            command.ByUserId,
            command.NowUtc
        );

    private static Result<FormAccessGrant> CreateGrant(
        AssignExternalFormRequestCommand command,
        FormRequest request,
        EntryNoticeSelection entryNotice,
        FormOpaqueToken token
    ) =>
        FormAccessGrant.Create(
            command.TenantId,
            request.Id,
            entryNotice.Notice.Id,
            entryNotice.Version.VersionNumber,
            token.TokenHash,
            token.TokenLast4,
            command.NowUtc.Add(GrantLifetime),
            command.NowUtc
        );

    private static async Task PublishAsync(
        AssignExternalFormRequestCommand command,
        PreparedAssignment assignment,
        IProtectedPayloadCodec payloadCodec,
        IMessageBus bus,
        ICorrelationContext correlation
    )
    {
        await bus.PublishAsync(
            FormRequestIntegrationEventFactory.Assigned(assignment.Request, correlation.CorrelationId)
        );
        await bus.PublishAsync(AccessLinkEvent(command, assignment, payloadCodec, correlation.CorrelationId));
    }

    private static FormAccessLinkDeliveryRequestedIntegrationEvent AccessLinkEvent(
        AssignExternalFormRequestCommand command,
        PreparedAssignment assignment,
        IProtectedPayloadCodec payloadCodec,
        string correlationId
    )
    {
        var participant = new ClientFormParticipant
        {
            Kind = nameof(FormParticipantKind.External),
            ParticipantId = assignment.Participant.Id,
        };
        var commandId = Guid.NewGuid();
        return new()
        {
            EventId = commandId,
            CommandId = commandId,
            TenantId = command.TenantId,
            OccurredOn = command.NowUtc,
            CorrelationId = correlationId,
            FormRequestId = assignment.Request.Id,
            Participant = participant,
            ExpiresAtUtc = assignment.Grant.ExpiresAtUtc,
            Payload = AccessLinkPayload(command.TenantId, assignment, participant, payloadCodec),
        };
    }

    private static ProtectedPayload AccessLinkPayload(
        Guid tenantId,
        PreparedAssignment assignment,
        ClientFormParticipant participant,
        IProtectedPayloadCodec payloadCodec
    ) =>
        payloadCodec.Protect(
            ClientFormProtectedPayloadSchemas.AccessLink,
            new ProtectedPayloadBinding(tenantId, assignment.Request.Id, participant),
            new FormAccessLinkDeliveryPayload(
                assignment.Participant.Email,
                assignment.Participant.FullName,
                assignment.Token.RawToken,
                assignment.Request.Title,
                assignment.Participant.Language
            )
        );

    private static void RecordAudit(AssignExternalFormRequestCommand command, Guid requestId, IFormAuditLog audit) =>
        audit.Record(command.TenantId, command.TemplateId, requestId, "request.assigned_external", command.ByUserId);

    private static Result<FormRequestResponse> Failure(Error error) => Result.Failure<FormRequestResponse>(error);
}
