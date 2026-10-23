using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
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
        IProtectedPayloadCodec payloadCodec,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var inputs = await LoadAndValidateAsync(command, capabilities, templates, versions, notices, ct);
        if (inputs.IsFailure)
            return Failure(inputs.Error);
        var participant = await ResolveParticipantAsync(command, inputs.Value.Email, participants, requests, ct);
        if (participant.IsFailure)
            return Failure(participant.Error);
        var request = CreateRequest(command, participant.Value, inputs.Value.Version);
        if (request.IsFailure)
            return Failure(request.Error);
        var token = security.GenerateToken();
        var grant = CreateGrant(command, request.Value, inputs.Value.Notice, token);
        if (grant.IsFailure)
            return Failure(grant.Error);
        requests.Add(request.Value);
        grants.Add(grant.Value);

        await PublishAsync(
            command,
            participant.Value,
            request.Value,
            token,
            grant.Value.ExpiresAtUtc,
            payloadCodec,
            audit,
            bus,
            correlation
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(FormRequestResponse.From(request.Value));
    }

    private static async Task<
        Result<(ParticipantEmail Email, FormTemplateVersion Version, EntryNoticeSelection Notice)>
    > LoadAndValidateAsync(
        AssignExternalFormRequestCommand command,
        IClientFormsPublicCapabilityEvaluator capabilities,
        IFormTemplateRepository templates,
        IFormTemplateVersionRepository versions,
        IConsentNoticeRepository notices,
        CancellationToken ct
    )
    {
        var email = await ValidateAsync(command, capabilities, ct);
        if (email.IsFailure)
            return Result.Failure<(ParticipantEmail, FormTemplateVersion, EntryNoticeSelection)>(email.Error);
        var version = await LoadPublishedVersionAsync(command, templates, versions, ct);
        if (version.IsFailure)
            return Result.Failure<(ParticipantEmail, FormTemplateVersion, EntryNoticeSelection)>(version.Error);
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
        IFormParticipantRepository participants,
        IFormRequestRepository requests,
        CancellationToken ct
    )
    {
        var participant = await GetOrCreateParticipantAsync(command, email, participants, ct);
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
                command.NowUtc
            );
            if (created.IsSuccess)
                participants.Add(created.Value);
            return created;
        }

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
        FormParticipant participant,
        FormRequest request,
        FormOpaqueToken token,
        DateTime expiresAtUtc,
        IProtectedPayloadCodec payloadCodec,
        IFormAuditLog audit,
        IMessageBus bus,
        ICorrelationContext correlation
    )
    {
        var messageParticipant = new ClientFormParticipant
        {
            Kind = nameof(FormParticipantKind.External),
            ParticipantId = participant.Id,
        };
        var commandId = Guid.NewGuid();
        var protectedPayload = payloadCodec.Protect(
            ClientFormProtectedPayloadSchemas.AccessLink,
            new ProtectedPayloadBinding(command.TenantId, request.Id, messageParticipant),
            new FormAccessLinkDeliveryPayload(participant.Email, participant.FullName, token.RawToken, request.Title)
        );

        audit.Record(command.TenantId, command.TemplateId, request.Id, "request.assigned_external", command.ByUserId);
        await bus.PublishAsync(FormRequestIntegrationEventFactory.Assigned(request, correlation.CorrelationId));
        await bus.PublishAsync(
            new FormAccessLinkDeliveryRequestedIntegrationEvent
            {
                EventId = commandId,
                CommandId = commandId,
                TenantId = command.TenantId,
                OccurredOn = command.NowUtc,
                CorrelationId = correlation.CorrelationId,
                FormRequestId = request.Id,
                Participant = messageParticipant,
                ExpiresAtUtc = expiresAtUtc,
                Payload = protectedPayload,
            }
        );
    }

    private static Result<FormRequestResponse> Failure(Error error) => Result.Failure<FormRequestResponse>(error);
}
