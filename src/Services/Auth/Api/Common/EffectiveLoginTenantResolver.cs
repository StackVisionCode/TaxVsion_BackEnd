using BuildingBlocks.Results;

namespace TaxVision.Auth.Api.Common;

/// <summary>
/// Fase A6 — el gap que hace real el aislamiento de login (v2 doc §26.2.2): antes de
/// esto, LoginCommand/ForgotPasswordCommand tomaban TenantId directo del body, así
/// que un cliente en tenantB.taxprocore.com podía mandar el TenantId de otro tenant
/// y el subdominio no importaba nada — el candidato de by-host era decorativo.
///
/// Con EnforceHostResolution=true (staging/producción) el TenantId del body se
/// descarta siempre: solo cuenta el que TenantHostResolutionMiddleware resolvió del
/// Host real. Con EnforceHostResolution=false (Development, sin subdominios reales
/// configurados localmente — ver doc §26.1) se acepta el TenantId explícito del
/// cliente como excepción de conveniencia para desarrollo.
/// </summary>
public static class EffectiveLoginTenantResolver
{
    public static Result<Guid> Resolve(bool enforceHostResolution, Guid? resolvedTenantId, Guid? requestedTenantId)
    {
        if (enforceHostResolution)
        {
            return resolvedTenantId is { } tenantId
                ? Result.Success(tenantId)
                : Result.Failure<Guid>(new Error("Tenant.NotFound", "Host does not resolve to an active tenant."));
        }

        return requestedTenantId is { } explicitTenantId
            ? Result.Success(explicitTenantId)
            : Result.Failure<Guid>(new Error("Auth.TenantIdRequired", "TenantId is required."));
    }
}
