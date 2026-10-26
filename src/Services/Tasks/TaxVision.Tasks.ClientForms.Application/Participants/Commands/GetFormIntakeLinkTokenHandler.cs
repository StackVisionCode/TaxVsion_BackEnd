using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Audit;
using TaxVision.Tasks.ClientForms.Domain.Participants;

namespace TaxVision.Tasks.ClientForms.Application.Participants.Commands;

// Revelar el token sí exige el flag: con el flag apagado el enlace no sirve y no hace falta exponer el secreto.
public static class GetFormIntakeLinkTokenHandler
{
    public static async Task<Result<FormIntakeLinkTokenResponse>> Handle(
        GetFormIntakeLinkTokenQuery query,
        IClientFormsPublicCapabilityEvaluator capabilities,
        IFormIntakeLinkRepository links,
        IFormExternalAccessSecurity security,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (!await capabilities.IsIntakeLinkAvailableAsync(query.TenantId, ct))
            return Result.Failure<FormIntakeLinkTokenResponse>(FormParticipantErrors.IntakeLinkUnavailable);
        var link = await links.GetAsync(query.TenantId, query.LinkId, ct);
        if (link is null)
            return Result.Failure<FormIntakeLinkTokenResponse>(FormParticipantErrors.IntakeLinkNotFound);
        var token = security.UnprotectToken(link.TokenCipher, link.TokenKeyVersion);
        audit.Record(query.TenantId, link.FormTemplateId, null, FormAuditAction.IntakeLinkTokenViewed, query.ByUserId);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(new FormIntakeLinkTokenResponse(FormIntakeLinkResponse.From(link), token));
    }
}
