using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Participants;

public static class FormExternalAccessRevoker
{
    // Al cerrar la request se cortan su enlace y sus sesiones públicas en la misma transacción.
    public static async Task RevokeAsync(
        FormRequest request,
        DateTime nowUtc,
        IFormAccessGrantRepository grants,
        IFormAccessSessionRepository sessions,
        CancellationToken ct
    )
    {
        if (request.Participant.Kind != FormParticipantKind.External)
            return;
        var grant = await grants.GetByRequestAsync(request.TenantId, request.Id, ct);
        grant?.Revoke(nowUtc);
        foreach (var session in await sessions.ListNotRevokedByRequestAsync(request.TenantId, request.Id, ct))
            session.Revoke(nowUtc);
    }
}
