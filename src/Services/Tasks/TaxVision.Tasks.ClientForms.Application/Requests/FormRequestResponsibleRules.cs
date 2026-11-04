using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;

namespace TaxVision.Tasks.ClientForms.Application.Requests;

// Quién puede ser responsable de una request: activo y con permiso de revisión.
public static class FormRequestResponsibleRules
{
    public const string ReviewPermission = "tasks.form_requests.review";

    public static async Task<bool> IsEligibleAsync(
        Guid tenantId,
        Guid userId,
        IFormResponsibleUserValidator responsibleUsers,
        CancellationToken ct
    ) =>
        userId != Guid.Empty
        && await responsibleUsers.IsActiveAsync(tenantId, userId, ct)
        && await responsibleUsers.HasPermissionsAsync(tenantId, userId, [ReviewPermission], ct);
}
