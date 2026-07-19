using System.Security.Claims;
using BuildingBlocks.Results;

namespace TaxVision.Tenant.Api.Common;

/// <summary>
/// Igual que EffectiveLoginTenantResolver en Auth: el claim del ticket firmado
/// ("reg_slug"/"reg_email", ver ReserveSubdomainHandler en Auth) es la única fuente de
/// verdad para Subdomain/AdminEmail cuando el request viene con ticket — el body nunca lo
/// sobreescribe. Sin ticket, cae al flujo PlatformAdmin y confía en el body (la policy
/// "TenantRegistration" ya exige uno de los dos para llegar hasta acá).
/// </summary>
public static class EffectiveTenantRegistrationResolver
{
    public sealed record Resolution(string Subdomain, string AdminEmail);

    public static Result<Resolution> Resolve(ClaimsPrincipal user, CreateTenantRequest request)
    {
        var ticketSlug = user.FindFirst("reg_slug")?.Value;
        if (!string.IsNullOrEmpty(ticketSlug))
        {
            var ticketEmail = user.FindFirst("reg_email")?.Value ?? string.Empty;
            return Result.Success(new Resolution(ticketSlug, ticketEmail));
        }

        if (string.IsNullOrWhiteSpace(request.Subdomain) || string.IsNullOrWhiteSpace(request.AdminEmail))
        {
            return Result.Failure<Resolution>(
                new Error("Tenant.RegistrationRequestInvalid", "Subdomain and AdminEmail are required.")
            );
        }

        return Result.Success(new Resolution(request.Subdomain, request.AdminEmail));
    }
}

public sealed record CreateTenantRequest(string Name, string? Subdomain, string? AdminEmail, string DefaultTimeZoneId);
