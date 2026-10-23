using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Audit;
using TaxVision.Tasks.ClientForms.Domain.Participants;

namespace TaxVision.Tasks.ClientForms.Application.Participants.Commands;

public static class UpdateFormIntakeLinkHandler
{
    public static async Task<Result<FormIntakeLinkTokenResponse>> Handle(
        UpdateFormIntakeLinkCommand command,
        IClientFormsPublicCapabilityEvaluator capabilities,
        IFormIntakeLinkRepository links,
        IFormTemplateVersionRepository versions,
        IFormExternalAccessSecurity security,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (
            RequiresAvailability(command.Action) && !await capabilities.IsIntakeLinkAvailableAsync(command.TenantId, ct)
        )
            return Failure(FormParticipantErrors.IntakeLinkUnavailable);
        var link = await links.GetAsync(command.TenantId, command.LinkId, ct);
        if (link is null)
            return Failure(FormParticipantErrors.IntakeLinkNotFound);
        if (!link.RowVersion.AsSpan().SequenceEqual(command.RowVersion))
            return Failure(FormParticipantErrors.IntakeLinkConcurrency);

        var token = command.Action == FormIntakeLinkAction.RegenerateToken ? security.GenerateProtectedToken() : null;
        var changed = await ApplyAsync(link, command, token, versions, ct);
        if (changed.IsFailure)
            return Failure(changed.Error);
        audit.Record(command.TenantId, link.FormTemplateId, null, FormAuditAction.IntakeLinkUpdated, command.ByUserId);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(new FormIntakeLinkTokenResponse(FormIntakeLinkResponse.From(link), token?.RawToken));
    }

    // Pausar o revocar siempre se permite: apagar un enlace no puede depender de que la captación esté disponible.
    private static bool RequiresAvailability(FormIntakeLinkAction action) =>
        action is not (FormIntakeLinkAction.Pause or FormIntakeLinkAction.Revoke);

    private static async Task<Result> ApplyAsync(
        FormIntakeLink link,
        UpdateFormIntakeLinkCommand command,
        FormProtectedToken? token,
        IFormTemplateVersionRepository versions,
        CancellationToken ct
    )
    {
        if (command.Action != FormIntakeLinkAction.PinVersion)
            return ApplySimple(link, command, token);
        if (!command.FormTemplateVersionId.HasValue)
            return Result.Failure(FormParticipantErrors.IntakeLinkInvalid);
        var version = await versions.GetByIdAsync(command.TenantId, command.FormTemplateVersionId.Value, ct);
        return version.IsFailure || version.Value.FormTemplateId != link.FormTemplateId
            ? Result.Failure(FormParticipantErrors.IntakeLinkInvalid)
            : link.PinVersion(version.Value.Id);
    }

    private static Result ApplySimple(
        FormIntakeLink link,
        UpdateFormIntakeLinkCommand command,
        FormProtectedToken? token
    ) =>
        command.Action switch
        {
            FormIntakeLinkAction.Pause => link.Pause(),
            FormIntakeLinkAction.Resume => link.Resume(command.NowUtc),
            FormIntakeLinkAction.Revoke => link.Revoke(),
            FormIntakeLinkAction.RegenerateToken when token is not null => link.RegenerateToken(
                token.TokenHash,
                token.TokenLast4,
                token.TokenCipher,
                token.TokenKeyVersion
            ),
            _ => Result.Failure(FormParticipantErrors.IntakeLinkInvalid),
        };

    private static Result<FormIntakeLinkTokenResponse> Failure(Error error) =>
        Result.Failure<FormIntakeLinkTokenResponse>(error);
}
