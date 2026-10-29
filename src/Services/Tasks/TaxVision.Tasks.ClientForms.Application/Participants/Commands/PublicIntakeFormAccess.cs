using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Participants;

namespace TaxVision.Tasks.ClientForms.Application.Participants.Commands;

internal static class PublicIntakeFormAccess
{
    internal static async Task<FormIntakeLink?> LoadAsync(
        string token,
        string requestHost,
        DateTime nowUtc,
        IFormIntakeLinkRepository links,
        IFormExternalAccessSecurity security,
        IFormPublicTenantHostValidator hosts,
        IClientFormsPublicCapabilityEvaluator capabilities,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(requestHost))
            return null;
        var link = await links.GetByTokenHashAsync(security.HashToken(token), ct);
        if (link is null || !link.IsAvailableAt(nowUtc))
            return null;
        if (!await hosts.MatchesTenantAsync(link.TenantId, requestHost, ct))
            return null;
        return await capabilities.IsIntakeLinkAvailableAsync(link.TenantId, ct) ? link : null;
    }
}
