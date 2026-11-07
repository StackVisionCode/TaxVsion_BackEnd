using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Mfa;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Offboarding;

/// <summary>Limpieza del retiro, con el acceso ya cortado: sin segundo factor ni invitaciones vivas a su nombre.</summary>
public sealed class OffboardingHygiene(IMfaRepository mfa, IInvitationRepository invitations, AuthAuditTrail audit)
{
    private const string InviterOffboardedDetails = """{"reason":"inviter_offboarded"}""";

    public async Task<OffboardingHygieneResult> RunAsync(User target, Guid actorUserId, CancellationToken ct)
    {
        var hadMfa = target.MfaEnabled;
        await MfaFactors.ClearAsync(target, mfa, ct);
        var cancelledInvitations = await CancelPendingInvitationsSentByAsync(target, actorUserId, ct);
        return new OffboardingHygieneResult(hadMfa, cancelledInvitations);
    }

    /// <summary>
    /// Cancela las invitaciones de staff pendientes que envió quien se va: llevan roles que él eligió y nadie
    /// en la oficina las vigila. Igual que la cancelación manual, no avisa al invitado (el enlace deja de valer);
    /// si la oficina lo quiere igual, lo invita de nuevo un admin. Las de portal no se tocan: son del cliente.
    /// </summary>
    private async Task<int> CancelPendingInvitationsSentByAsync(User target, Guid actorUserId, CancellationToken ct)
    {
        var cancelled = 0;
        var now = DateTime.UtcNow;
        foreach (var invitation in await invitations.ListPendingStaffSentByAsync(target.TenantId, target.Id, ct))
        {
            if (invitation.Cancel(actorUserId, now).IsFailure)
                continue;

            await audit.RecordAsync(
                invitation.TenantId,
                actorUserId,
                AuthAuditAction.InvitationCancelled,
                true,
                ct,
                targetType: "Invitation",
                targetId: invitation.Id,
                detailsJson: InviterOffboardedDetails
            );
            cancelled++;
        }

        return cancelled;
    }
}

public sealed record OffboardingHygieneResult(bool MfaCleared, int CancelledInvitations);
