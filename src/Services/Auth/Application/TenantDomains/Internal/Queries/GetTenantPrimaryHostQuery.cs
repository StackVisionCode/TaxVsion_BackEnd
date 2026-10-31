using BuildingBlocks.Platform;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.TenantDomains;

namespace TaxVision.Auth.Application.TenantDomains.Internal.Queries;

public sealed record GetTenantPrimaryHostQuery(Guid TenantId);

/// <param name="TimeZoneId">F6 (Communication): zona IANA de la oficina para los correos de reuniones; null si el registro no la tiene.</param>
public sealed record TenantPrimaryHostResponse(string Host, string? TimeZoneId = null);

/// <summary>
/// Host primario (subdominio de plataforma) de un tenant, ej. "manfer.taxoffix.io" — pull M2M
/// que llama Notification para armar los links per-tenant de los correos (staff en {host}, cliente
/// en {host}/portal). Auth es la autoridad del dominio; el subdominio no viaja en los eventos de
/// Tasks, así que se resuelve acá. Mismo patrón interno que
/// <see cref="TaxVision.Auth.Application.Users.Internal.Queries.GetUserContactHandler"/>.
///
/// <para>
/// Se prefiere el subdominio de plataforma primario y activo. Los custom hostnames no se devuelven:
/// el portal del cliente vive bajo el subdominio de plataforma (donde está el ruteo /portal), no en
/// el dominio propio de la oficina.
/// </para>
///
/// <para>El host se arma con el slug y el dominio vigente de la plataforma, no con el que quedó guardado:
/// así los links de los correos siguen el dominio actual aunque la fila no se haya re-apuntado todavía.</para>
/// </summary>
public static class GetTenantPrimaryHostHandler
{
    public static async Task<Result<TenantPrimaryHostResponse>> Handle(
        GetTenantPrimaryHostQuery query,
        ITenantDomainRepository domains,
        IPlatformSettings platform,
        ITenantRegistry tenants,
        CancellationToken ct
    )
    {
        var all = await domains.GetByTenantAsync(query.TenantId, ct);
        // F6: la zona horaria viaja junto al host (misma consulta M2M) para que Communication la ponga en sus eventos.
        var timeZoneId = (await tenants.GetByIdAsync(query.TenantId, ct))?.DefaultTimeZoneId;

        var primary =
            all.FirstOrDefault(d =>
                d.DomainType == TenantDomainType.Subdomain && d.IsPrimary && d.Status == TenantDomainStatus.Active
            )
            ?? all.FirstOrDefault(d =>
                d.DomainType == TenantDomainType.Subdomain && d.Status == TenantDomainStatus.Active
            );

        return primary?.SubdomainSlug is null
            ? Result.Failure<TenantPrimaryHostResponse>(
                new Error("TenantDomain.NoPrimaryHost", "The tenant has no active platform subdomain.")
            )
            : Result.Success(
                new TenantPrimaryHostResponse(platform.Current.TenantHost(primary.SubdomainSlug), timeZoneId)
            );
    }
}
