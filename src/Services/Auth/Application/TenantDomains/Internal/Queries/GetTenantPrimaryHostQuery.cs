using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.TenantDomains;

namespace TaxVision.Auth.Application.TenantDomains.Internal.Queries;

public sealed record GetTenantPrimaryHostQuery(Guid TenantId);

public sealed record TenantPrimaryHostResponse(string Host);

/// <summary>
/// Host primario (subdominio de plataforma) de un tenant, ej. "manfer.taxproffice.com" — pull M2M
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
/// </summary>
public static class GetTenantPrimaryHostHandler
{
    public static async Task<Result<TenantPrimaryHostResponse>> Handle(
        GetTenantPrimaryHostQuery query,
        ITenantDomainRepository domains,
        CancellationToken ct
    )
    {
        var all = await domains.GetByTenantAsync(query.TenantId, ct);

        var primary =
            all.FirstOrDefault(d =>
                d.DomainType == TenantDomainType.Subdomain
                && d.IsPrimary
                && d.Status == TenantDomainStatus.Active
            )
            ?? all.FirstOrDefault(d =>
                d.DomainType == TenantDomainType.Subdomain && d.Status == TenantDomainStatus.Active
            );

        return primary is null
            ? Result.Failure<TenantPrimaryHostResponse>(
                new Error("TenantDomain.NoPrimaryHost", "The tenant has no active platform subdomain.")
            )
            : Result.Success(new TenantPrimaryHostResponse(primary.Host));
    }
}
