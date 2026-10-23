using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Consents;
using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Audit;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.ClientForms.Domain.Versions;

namespace TaxVision.Tasks.ClientForms.Application.Participants.Commands;

public static class CreateFormIntakeLinkHandler
{
    public static async Task<Result<FormIntakeLinkTokenResponse>> Handle(
        CreateFormIntakeLinkCommand command,
        IClientFormsPublicCapabilityEvaluator capabilities,
        IFormResponsibleUserValidator responsibleUsers,
        IFormTemplateRepository templates,
        IFormTemplateVersionRepository versions,
        IConsentNoticeRepository notices,
        IFormIntakeLinkRepository links,
        IFormExternalAccessSecurity security,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (!await capabilities.IsIntakeLinkAvailableAsync(command.TenantId, ct))
            return Failure(FormParticipantErrors.IntakeLinkUnavailable);
        if (!await responsibleUsers.IsActiveAsync(command.TenantId, command.ResponsibleUserId, ct))
            return Failure(FormParticipantErrors.IntakeResponsibleUnavailable);
        var sources = await LoadSourcesAsync(command, templates, versions, notices, ct);
        if (sources.IsFailure)
            return Failure(sources.Error);

        var token = security.GenerateProtectedToken();
        var created = CreateLink(command, sources.Value.Version, sources.Value.Notice, token);
        if (created.IsFailure)
            return Failure(created.Error);
        links.Add(created.Value);
        audit.Record(
            command.TenantId,
            command.FormTemplateId,
            null,
            FormAuditAction.IntakeLinkCreated,
            command.ByUserId
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(
            new FormIntakeLinkTokenResponse(FormIntakeLinkResponse.From(created.Value), token.RawToken)
        );
    }

    private static async Task<Result<(FormTemplateVersion Version, EntryNoticeSelection Notice)>> LoadSourcesAsync(
        CreateFormIntakeLinkCommand command,
        IFormTemplateRepository templates,
        IFormTemplateVersionRepository versions,
        IConsentNoticeRepository notices,
        CancellationToken ct
    )
    {
        var version = await LoadVersionAsync(command, templates, versions, ct);
        if (version.IsFailure)
            return Result.Failure<(FormTemplateVersion, EntryNoticeSelection)>(version.Error);
        var entryNotice = await EntryNoticeSupport.ResolveForCreationAsync(
            command.TenantId,
            command.EntryNoticeKey,
            notices,
            ct
        );
        return entryNotice is null
            ? Result.Failure<(FormTemplateVersion, EntryNoticeSelection)>(FormParticipantErrors.EntryNoticeRequired)
            : Result.Success((version.Value, entryNotice));
    }

    // La versión fijada debe ser de la misma plantilla; sin versión explícita se usa la última.
    private static async Task<Result<FormTemplateVersion>> LoadVersionAsync(
        CreateFormIntakeLinkCommand command,
        IFormTemplateRepository templates,
        IFormTemplateVersionRepository versions,
        CancellationToken ct
    )
    {
        var template = await templates.GetByIdAsync(command.TenantId, command.FormTemplateId, ct);
        if (template.IsFailure || template.Value.Status != FormTemplateStatus.Active)
            return Result.Failure<FormTemplateVersion>(FormParticipantErrors.IntakeLinkInvalid);
        var versionId = command.FormTemplateVersionId ?? template.Value.LatestVersionId;
        if (!versionId.HasValue)
            return Result.Failure<FormTemplateVersion>(FormParticipantErrors.IntakeLinkInvalid);
        var version = await versions.GetByIdAsync(command.TenantId, versionId.Value, ct);
        return version.IsFailure || version.Value.FormTemplateId != command.FormTemplateId
            ? Result.Failure<FormTemplateVersion>(FormParticipantErrors.IntakeLinkInvalid)
            : version;
    }

    private static Result<FormIntakeLink> CreateLink(
        CreateFormIntakeLinkCommand command,
        FormTemplateVersion version,
        EntryNoticeSelection entryNotice,
        FormProtectedToken token
    ) =>
        FormIntakeLink.Create(
            command.TenantId,
            command.Name,
            command.FormTemplateId,
            version.Id,
            entryNotice.Notice.Id,
            entryNotice.Version.VersionNumber,
            token.TokenHash,
            token.TokenLast4,
            token.TokenCipher,
            token.TokenKeyVersion,
            command.ExpiresAtUtc,
            command.MaxSubmissions,
            command.ResponsibleUserId,
            command.ByUserId,
            command.NowUtc
        );

    private static Result<FormIntakeLinkTokenResponse> Failure(Error error) =>
        Result.Failure<FormIntakeLinkTokenResponse>(error);
}
