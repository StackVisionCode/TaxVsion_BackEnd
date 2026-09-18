using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Invitations;

namespace TaxVision.Auth.Application.Invitations.Queries;

/// <summary>Validación ANÓNIMA de una invitación por su token, para que la página de "aceptar invitación"
/// (empleado o cliente) sepa ANTES de mostrar el formulario: (a) si el token sigue siendo hábil, y (b) a qué
/// oficina pertenece — así puede pintar el branding de la oficina REAL del token (no la del subdominio de la
/// URL) y mostrar un mensaje profesional si ya se usó/expiró/canceló. Solo lee; nunca muta ni consume el token.
/// No filtra por tenant (el token es la credencial). Devuelve solo lo que quien tiene el token ya conoce
/// (email destinatario + oficina), así que no filtra nada nuevo.</summary>
public sealed record ValidateInvitationQuery(string Token);

/// <summary>Estado efectivo de cara al frontend: <c>Pending</c> muestra el formulario; el resto muestra la
/// pantalla "esta invitación ya no es válida". <c>Invalid</c> = token no encontrado.</summary>
public sealed record InvitationValidationResponse(
    string Status,
    string? Email,
    string? ActorType,
    InvitationTenantBrand? Tenant
);

/// <summary>Datos mínimos de la oficina para pintar el branding correcto (por subdominio) en la página.</summary>
public sealed record InvitationTenantBrand(Guid Id, string Name, string SubDomain);

public static class ValidateInvitationHandler
{
    private const string InvalidStatus = "Invalid";

    public static async Task<Result<InvitationValidationResponse>> Handle(
        ValidateInvitationQuery query,
        IInvitationRepository invitations,
        IInvitationTokenService tokens,
        ITenantRegistry tenants,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(query.Token))
            return Result.Success(new InvitationValidationResponse(InvalidStatus, null, null, null));

        var tokenHash = tokens.Hash(query.Token);
        var invitation = await invitations.GetByTokenHashAsync(tokenHash, ct);
        if (invitation is null || !invitation.MatchesTokenHash(tokenHash))
            return Result.Success(new InvitationValidationResponse(InvalidStatus, null, null, null));

        // Expiración calculada, sin persistir (es una lectura): una invitación Pending vencida se reporta
        // como Expired para que el front muestre "ya no es válida".
        var effectiveStatus =
            invitation.Status == InvitationStatus.Pending && invitation.ExpiresAtUtc <= DateTime.UtcNow
                ? nameof(InvitationStatus.Expired)
                : invitation.Status.ToString();

        var tenant = await tenants.GetByIdAsync(invitation.TenantId, ct);
        var brand = tenant is null ? null : new InvitationTenantBrand(tenant.Id, tenant.Name, tenant.SubDomain);

        return Result.Success(
            new InvitationValidationResponse(effectiveStatus, invitation.Email, invitation.ActorType.ToString(), brand)
        );
    }
}
