using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.TenantDomains.Internal.Queries;
using TaxVision.Auth.Domain.Credentials;

namespace TaxVision.Auth.Application.Credentials.Queries;

/// <summary>Validación ANÓNIMA del enlace de reset. Devuelve si el token todavía sirve y a qué oficina
/// pertenece — así la página puede: (a) pintar el branding de la oficina REAL del token (no la del subdominio
/// de la URL), y (b) rebotar al host correcto cuando el enlace se abrió en otro subdominio. Solo lee; nunca
/// consume el token ni cuenta intentos. Devuelve solo lo que quien tiene el token ya conoce (su oficina), así
/// que no filtra nada nuevo. Espejo semántico de <c>ValidateInvitationQuery</c>.</summary>
public sealed record ValidatePasswordResetTokenQuery(string Token);

/// <summary>Estado de cara al frontend: <c>Valid</c> muestra el formulario; <c>Completed</c>, <c>Expired</c>,
/// <c>Cancelled</c> y <c>Superseded</c> explican por qué ya no sirve (03 §8.4); <c>Invalid</c> es la pantalla genérica.
/// Los estados granulares, <c>Tenant</c>, <c>Origin</c> y <c>MfaEnabled</c> solo salen cuando el hash coincide con un
/// link real. <c>MfaEnabled</c> deja al front pedir 12 o 15 caracteres (D-13).</summary>
public sealed record PasswordResetValidationResponse(
    string Status,
    PasswordResetTenantBrand? Tenant,
    string? Origin = null,
    bool? MfaEnabled = null
);

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
        ITenantDomainRepository domains,
        CancellationToken ct
    )
    {
        var invalid = Result.Success(new PasswordResetValidationResponse(InvalidStatus, null));
        if (string.IsNullOrWhiteSpace(query.Token))
            return invalid;
        var resetToken = await credentials.GetPasswordResetByHashAsync(tokens.Hash(query.Token), ct);
        if (resetToken is null)
            return invalid;
        // Cuenta desactivada o retirada: genérico, sin confirmar el estado de la cuenta.
        var user = await users.GetByIdAsync(resetToken.UserId, ct);
        if (user is not { IsActive: true })
            return invalid;
        // Revocado por cambio de contraseña o por asegurar la cuenta: genérico (anti-enumeración).
        if (ToWireStatus(resetToken.StatusAt(DateTime.UtcNow)) is not { } status)
            return invalid;

        // Mismo host que el link del correo (subdominio primario activo); sin él no hay oficina a la que rebotar.
        var primary = GetTenantPrimaryHostHandler.PickPrimarySubdomain(
            await domains.GetByTenantAsync(resetToken.TenantId, ct)
        );
        if (primary?.SubdomainSlug is null)
            return invalid;

        var tenant = await tenantRegistry.GetByIdAsync(resetToken.TenantId, ct);
        var brand = tenant is null ? null : new PasswordResetTenantBrand(tenant.Id, tenant.Name, primary.SubdomainSlug);
        return Result.Success(
            new PasswordResetValidationResponse(status, brand, resetToken.Origin.ToString(), user.MfaEnabled)
        );
    }

    // Agotado se informa como vencido (03 §8.4).
    private static string? ToWireStatus(PasswordResetTokenStatus status) =>
        status switch
        {
            PasswordResetTokenStatus.Pending => ValidStatus,
            PasswordResetTokenStatus.Completed => "Completed",
            PasswordResetTokenStatus.Expired or PasswordResetTokenStatus.Exhausted => "Expired",
            PasswordResetTokenStatus.Cancelled => "Cancelled",
            PasswordResetTokenStatus.Superseded => "Superseded",
            _ => null,
        };
}
