using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;

namespace TaxVision.Tasks.ClientForms.Application.Offboarding;

// Pre-flight del retiro: enlaces de captación no revocados y requests abiertas que hoy dependen del empleado.
public sealed record ClientFormsOffboardingImpactQuery(Guid TenantId, Guid UserId);

public sealed record ClientFormsOffboardingImpactResponse(int IntakeLinks, int OpenFormRequests);

public static class ClientFormsOffboardingImpactHandler
{
    public static async Task<ClientFormsOffboardingImpactResponse> Handle(
        ClientFormsOffboardingImpactQuery query,
        IFormIntakeLinkRepository links,
        IFormRequestRepository requests,
        CancellationToken ct
    )
    {
        var intakeLinks = await links.CountNotRevokedByResponsibleAsync(query.TenantId, query.UserId, ct);
        var openRequests = await requests.CountOpenByEffectiveResponsibleAsync(query.TenantId, query.UserId, ct);
        return new ClientFormsOffboardingImpactResponse(intakeLinks, openRequests);
    }
}
