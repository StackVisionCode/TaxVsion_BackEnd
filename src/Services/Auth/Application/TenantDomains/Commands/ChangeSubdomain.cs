using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.TenantDomains;

namespace TaxVision.Auth.Application.TenantDomains.Commands;

/// <summary>
/// Fase A7 — le permite al tenant admin cambiar el subdominio primario ya activo
/// (ej. oficina1.taxprocore.com -&gt; oficina2.taxprocore.com). Solo aplica a
/// DomainType.Subdomain: un custom hostname no se renombra por acá, ver
/// TenantDomain.ChangeSubdomain.
/// </summary>
public sealed record ChangeSubdomainCommand(Guid TenantId, Guid DomainId, string? NewSlug, Guid ActingUserId);

public static class ChangeSubdomainHandler
{
    public static async Task<Result<TenantDomainResponse>> Handle(
        ChangeSubdomainCommand command,
        ITenantDomainRepository domains,
        ITenantSubdomainReservationRepository reservations,
        IOptions<TenantDomainOptions> options,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var domainResult = await TenantDomainAccessGuard.LoadOwnedAsync(
            domains,
            command.TenantId,
            command.DomainId,
            ct
        );
        if (domainResult.IsFailure)
            return Result.Failure<TenantDomainResponse>(domainResult.Error);

        var slugResult = SubdomainSlug.Create(command.NewSlug);
        if (slugResult.IsFailure)
            return Result.Failure<TenantDomainResponse>(slugResult.Error);

        var slug = slugResult.Value;
        var domain = domainResult.Value;
        var nowUtc = DateTime.UtcNow;

        // El slug propio no cuenta como "ya tomado" — lo distingue el agregado (SlugUnchanged).
        if (slug.Value != domain.SubdomainSlug && await domains.SlugExistsAsync(slug.Value, ct))
            return Result.Failure<TenantDomainResponse>(
                new Error("TenantDomain.SlugTaken", "This subdomain is already in use.")
            );

        if (await reservations.GetActiveBySlugAsync(slug.Value, nowUtc, ct) is not null)
            return Result.Failure<TenantDomainResponse>(
                new Error("TenantDomain.SlugReservedTemporarily", "This subdomain is temporarily reserved.")
            );

        var changeResult = domain.ChangeSubdomain(slug, options.Value.BaseDomain, command.ActingUserId);
        if (changeResult.IsFailure)
            return Result.Failure<TenantDomainResponse>(changeResult.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(TenantDomainResponse.From(domain));
    }
}
