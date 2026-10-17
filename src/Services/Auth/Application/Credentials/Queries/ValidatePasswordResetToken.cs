using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Application.Credentials.Queries;

/// <summary>Validación ANÓNIMA del enlace de reset. Devuelve si el token todavía sirve y a qué oficina
/// pertenece — así la página puede: (a) pintar el branding de la oficina REAL del token (no la del subdominio
/// de la URL), y (b) rebotar al host correcto cuando el enlace se abrió en otro subdominio. Solo lee; nunca
/// consume el token ni cuenta intentos. Devuelve solo lo que quien tiene el token ya conoce (su oficina), así
/// que no filtra nada nuevo. Espejo semántico de <c>ValidateInvitationQuery</c>.</summary>
public sealed record ValidatePasswordResetTokenQuery(string Token);

/// <summary>Estado de cara al frontend: <c>Valid</c> muestra el formulario; <c>Invalid</c> muestra la pantalla
/// "este enlace ya no es válido". Para no filtrar nada, <c>Tenant</c> solo se incluye en <c>Valid</c>.</summary>
public sealed record PasswordResetValidationResponse(string Status, PasswordResetTenantBrand? Tenant);

/// <summary>Datos mínimos de la oficina para pintar branding + rebotar al subdominio correcto.</summary>
public sealed record PasswordResetTenantBrand(Guid Id, string Name, string SubDomain);

public static class ValidatePasswordResetTokenHandler
{
    private const string ValidStatus = "Valid";
    private const string InvalidStatus = "Invalid";

    public static async Task<Result<PasswordResetValidationResponse>> Handle(
        ValidatePasswordResetTokenQuery query,
        ICredentialTokenRepository credentials,
        ISecureTokenService tokens,
        IUserRepository users,
        ITenantRegistry tenantRegistry,
        CancellationToken ct
    )
    {
        var found = await PasswordResetLinks.FindUsableAsync(
            query.Token,
            credentials,
            tokens,
            users,
            DateTime.UtcNow,
            ct
        );
        if (found is not { } usable)
            return Result.Success(new PasswordResetValidationResponse(InvalidStatus, null));

        var (resetToken, _) = usable;
        var tenant = await tenantRegistry.GetByIdAsync(resetToken.TenantId, ct);
        var brand = tenant is null ? null : new PasswordResetTenantBrand(tenant.Id, tenant.Name, tenant.SubDomain);
        return Result.Success(new PasswordResetValidationResponse(ValidStatus, brand));
    }
}
