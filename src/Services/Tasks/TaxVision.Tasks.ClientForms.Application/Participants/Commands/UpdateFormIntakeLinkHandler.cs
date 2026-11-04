using System.Text.Json;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Actions.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Audit;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.ClientForms.Domain.Versions;

namespace TaxVision.Tasks.ClientForms.Application.Participants.Commands;

public static class UpdateFormIntakeLinkHandler
{
    public static async Task<Result<FormIntakeLinkTokenResponse>> Handle(
        UpdateFormIntakeLinkCommand command,
        IClientFormsPublicCapabilityEvaluator capabilities,
        IFormIntakeLinkRepository links,
        IFormTemplateVersionRepository versions,
        IFormResponsibleUserValidator responsibleUsers,
        IFormActionAuthorizer authorizer,
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
        var previousResponsible = link.ResponsibleUserId;
        var changed = await ApplyAsync(link, command, token, new(versions, responsibleUsers, authorizer), ct);
        if (changed.IsFailure)
            return Failure(changed.Error);
        RecordAudit(audit, command, link, previousResponsible);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(new FormIntakeLinkTokenResponse(FormIntakeLinkResponse.From(link), token?.RawToken));
    }

    // Pausar o revocar siempre se permite: apagar un enlace no puede depender de que la captación esté disponible.
    private static bool RequiresAvailability(FormIntakeLinkAction action) =>
        action is not (FormIntakeLinkAction.Pause or FormIntakeLinkAction.Revoke);

    private static void RecordAudit(
        IFormAuditLog audit,
        UpdateFormIntakeLinkCommand command,
        FormIntakeLink link,
        Guid previousResponsible
    )
    {
        if (command.Action != FormIntakeLinkAction.ChangeResponsible)
        {
            audit.Record(
                command.TenantId,
                link.FormTemplateId,
                null,
                FormAuditAction.IntakeLinkUpdated,
                command.ByUserId
            );
            return;
        }
        var details = JsonSerializer.Serialize(
            new
            {
                linkId = link.Id,
                previousResponsibleUserId = previousResponsible,
                link.ResponsibleUserId,
            }
        );
        audit.Record(
            command.TenantId,
            link.FormTemplateId,
            null,
            FormAuditAction.IntakeLinkResponsibleChanged,
            command.ByUserId,
            details
        );
    }

    private static Task<Result> ApplyAsync(
        FormIntakeLink link,
        UpdateFormIntakeLinkCommand command,
        FormProtectedToken? token,
        PinSources sources,
        CancellationToken ct
    ) =>
        command.Action switch
        {
            FormIntakeLinkAction.PinVersion => ApplyPinAsync(link, command, sources, ct),
            FormIntakeLinkAction.ChangeResponsible => ApplyResponsibleAsync(link, command, sources, ct),
            FormIntakeLinkAction.Resume => ApplyResumeAsync(link, command, sources, ct),
            _ => Task.FromResult(ApplySimple(link, command, token)),
        };

    // Reanudar con un responsable inactivo (p. ej. retirado) haría fallar cada lead: primero hay que reasignarlo.
    private static async Task<Result> ApplyResumeAsync(
        FormIntakeLink link,
        UpdateFormIntakeLinkCommand command,
        PinSources sources,
        CancellationToken ct
    ) =>
        await sources.ResponsibleUsers.IsActiveAsync(link.TenantId, link.ResponsibleUserId, ct)
            ? link.Resume(command.NowUtc)
            : Result.Failure(FormParticipantErrors.IntakeResponsibleUnavailable);

    private static async Task<Result> ApplyPinAsync(
        FormIntakeLink link,
        UpdateFormIntakeLinkCommand command,
        PinSources sources,
        CancellationToken ct
    )
    {
        if (!command.FormTemplateVersionId.HasValue)
            return Result.Failure(FormParticipantErrors.IntakeLinkInvalid);
        var version = await sources.Versions.GetByIdAsync(command.TenantId, command.FormTemplateVersionId.Value, ct);
        if (version.IsFailure || version.Value.FormTemplateId != link.FormTemplateId)
            return Result.Failure(FormParticipantErrors.IntakeLinkInvalid);
        // La versión nueva puede traer acciones que el responsable actual no puede ejecutar.
        var responsible = await EnsureResponsibleAsync(link, link.ResponsibleUserId, version.Value, sources, ct);
        return responsible.IsFailure ? responsible : link.PinVersion(version.Value.Id);
    }

    // El nuevo responsable debe poder ejecutar las acciones de la versión fijada en el enlace.
    private static async Task<Result> ApplyResponsibleAsync(
        FormIntakeLink link,
        UpdateFormIntakeLinkCommand command,
        PinSources sources,
        CancellationToken ct
    )
    {
        if (command.ResponsibleUserId is not { } responsibleUserId || responsibleUserId == Guid.Empty)
            return Result.Failure(FormParticipantErrors.IntakeLinkInvalid);
        var version = await sources.Versions.GetByIdAsync(command.TenantId, link.FormTemplateVersionId, ct);
        if (version.IsFailure)
            return Result.Failure(FormParticipantErrors.IntakeLinkInvalid);
        var responsible = await EnsureResponsibleAsync(link, responsibleUserId, version.Value, sources, ct);
        return responsible.IsFailure ? responsible : link.ReassignResponsible(responsibleUserId);
    }

    private static Task<Result> EnsureResponsibleAsync(
        FormIntakeLink link,
        Guid responsibleUserId,
        FormTemplateVersion version,
        PinSources sources,
        CancellationToken ct
    ) =>
        FormIntakeLinkResponsibleRules.EnsureEligibleAsync(
            link.TenantId,
            responsibleUserId,
            version,
            sources.ResponsibleUsers,
            sources.Authorizer,
            ct
        );

    private sealed record PinSources(
        IFormTemplateVersionRepository Versions,
        IFormResponsibleUserValidator ResponsibleUsers,
        IFormActionAuthorizer Authorizer
    );

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
