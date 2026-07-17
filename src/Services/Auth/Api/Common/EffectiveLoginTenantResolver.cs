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
/// Host real. Con EnforceHostResolution=false (Development) se acepta el TenantId
/// explícito del cliente como excepción de conveniencia — pero SOLO cuando el Host
/// realmente no resolvió a nada. Si el Host sí resolvió (ej. un subdominio local real
/// registrado en TenantDomains, como demo.localhost apuntando a un tenant real), ese
/// resultado real siempre gana sobre el body — no tiene sentido preferir un TenantId
/// manual cuando ya hay uno confiable disponible. Antes de este fix, con
/// EnforceHostResolution=false, un Host resuelto se descartaba igual y SIEMPRE se
/// exigía el campo manual — un dev-escape-hatch que nunca contempló que localmente sí
/// pudiera existir una resolución real.
/// </summary>
public static class EffectiveLoginTenantResolver
{
    public static Result<Guid> Resolve(bool enforceHostResolution, Guid? resolvedTenantId, Guid? requestedTenantId)
    {
        if (resolvedTenantId is { } tenantId)
            return Result.Success(tenantId);

        if (enforceHostResolution)
            return Result.Failure<Guid>(new Error("Tenant.NotFound", "Host does not resolve to an active tenant."));

        return requestedTenantId is { } explicitTenantId
            ? Result.Success(explicitTenantId)
            : Result.Failure<Guid>(new Error("Auth.TenantIdRequired", "TenantId is required."));
    }
}
