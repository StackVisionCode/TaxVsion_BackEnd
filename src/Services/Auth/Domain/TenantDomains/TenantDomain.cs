using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Auth.Domain.TenantDomains.Events;

namespace TaxVision.Auth.Domain.TenantDomains;

/// <summary>Tipo de dominio: subdominio de la plataforma (wildcard) o dominio propio del tenant.</summary>
public enum TenantDomainType
{
    Subdomain,
    CustomHostname,
}

/// <summary>
/// Ciclo de vida del dominio. Subdominios wildcard pasan directo a Active (el
/// certificado wildcard ya los cubre, ver Fase A5); los custom hostnames transitan
/// Pending -&gt; Provisioning -&gt; Active mientras Cloudflare valida el CNAME/TXT del tenant.
/// </summary>
public enum TenantDomainStatus
{
    Pending,
    Provisioning,
    Active,
    Disabled,
    Failed,
}

/// <summary>
/// Dominio (subdominio de plataforma o dominio propio) que resuelve a un tenant. Auth es
/// la autoridad del ciclo de vida completo — ver Auth_y_CloudStorage_Plan_Completitud_v2.md
/// §10. La resolución Host→TenantId (Fase A3) consulta esta tabla.
/// </summary>
public sealed class TenantDomain : AggregateRoot
{
    private TenantDomain() { }

    public TenantDomainType DomainType { get; private set; }

    /// <summary>Host completo normalizado (ej. "oficina1.taxprocore.com"). Único globalmente.</summary>
    public string Host { get; private set; } = default!;

    /// <summary>Solo para DomainType.Subdomain — el slug elegido por la oficina.</summary>
    public string? SubdomainSlug { get; private set; }

    public TenantDomainStatus Status { get; private set; }

    /// <summary>El dominio principal con el que el tenant entra por defecto (uno por tenant).</summary>
    public bool IsPrimary { get; private set; }

    /// <summary>Id del custom hostname en Cloudflare — solo DomainType.CustomHostname (Fase A5).</summary>
    public string? CloudflareCustomHostnameId { get; private set; }

    public string? VerificationMethod { get; private set; }
    public DateTime? VerifiedAtUtc { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>
    /// Crea un subdominio de plataforma (ej. oficina1.taxprocore.com). Con wildcard DNS
    /// (Fase A5) el certificado ya cubre el host, así que arranca directo en Active —
    /// no hay paso de provisioning para este tipo.
    /// </summary>
    public static Result<TenantDomain> CreateSubdomain(
        Guid tenantId,
        SubdomainSlug slug,
        string baseDomain,
        Guid createdByUserId,
        DateTime nowUtc,
        bool isPrimary = true
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<TenantDomain>(new Error("TenantDomain.Tenant", "Tenant is required."));

        if (string.IsNullOrWhiteSpace(baseDomain))
            return Result.Failure<TenantDomain>(new Error("TenantDomain.BaseDomain", "Base domain is required."));

        var domain = new TenantDomain
        {
            DomainType = TenantDomainType.Subdomain,
            SubdomainSlug = slug.Value,
            Host = $"{slug.Value}.{baseDomain.Trim().ToLowerInvariant()}",
            Status = TenantDomainStatus.Active,
            IsPrimary = isPrimary,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = nowUtc,
        };
        domain.SetTenant(tenantId);
        domain.AddDomainEvent(
            new TenantDomainCreated(
                tenantId,
                domain.Id,
                domain.Host,
                domain.DomainType.ToString(),
                NormalizeActor(createdByUserId)
            )
        );

        return Result.Success(domain);
    }

    /// <summary>
    /// Crea un dominio propio del tenant (ej. archivos.suoficina.com), para
    /// Cloudflare for SaaS / Custom Hostnames (Fase A5). Arranca en Pending hasta que se
    /// dispare el provisioning.
    /// </summary>
    public static Result<TenantDomain> CreateCustomHostname(
        Guid tenantId,
        string host,
        Guid createdByUserId,
        DateTime nowUtc
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<TenantDomain>(new Error("TenantDomain.Tenant", "Tenant is required."));

        var normalizedHost = host?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalizedHost.Length is 0 or > 253 || !normalizedHost.Contains('.'))
            return Result.Failure<TenantDomain>(new Error("TenantDomain.HostInvalid", "Host is invalid."));

        var domain = new TenantDomain
        {
            DomainType = TenantDomainType.CustomHostname,
            Host = normalizedHost,
            Status = TenantDomainStatus.Pending,
            IsPrimary = false,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = nowUtc,
        };
        domain.SetTenant(tenantId);
        domain.AddDomainEvent(
            new TenantDomainCreated(
                tenantId,
                domain.Id,
                domain.Host,
                domain.DomainType.ToString(),
                NormalizeActor(createdByUserId)
            )
        );

        return Result.Success(domain);
    }

    /// <summary>Registra el custom hostname creado en Cloudflare y pasa a Provisioning (Fase A5).</summary>
    public Result MarkProvisioning(string cloudflareCustomHostnameId, string verificationMethod)
    {
        if (DomainType != TenantDomainType.CustomHostname)
        {
            return Result.Failure(
                new Error("TenantDomain.NotCustomHostname", "Only custom hostnames go through provisioning.")
            );
        }

        if (Status is not (TenantDomainStatus.Pending or TenantDomainStatus.Failed))
        {
            return Result.Failure(
                new Error("TenantDomain.InvalidTransition", "Domain is not in a state that can start provisioning.")
            );
        }

        CloudflareCustomHostnameId = cloudflareCustomHostnameId;
        VerificationMethod = verificationMethod;
        Status = TenantDomainStatus.Provisioning;
        return Result.Success();
    }

    /// <summary>Cloudflare confirmó status=active y ssl.status=active (Fase A5). Cubre verificación + activación en un solo paso (Fase A6/A7).</summary>
    public Result MarkActive(DateTime nowUtc, Guid? actingUserId = null)
    {
        if (Status is TenantDomainStatus.Disabled)
            return Result.Failure(new Error("TenantDomain.Disabled", "Domain has been disabled."));

        Status = TenantDomainStatus.Active;
        VerifiedAtUtc = nowUtc;
        AddDomainEvent(new TenantDomainActivated(TenantId, Id, Host, actingUserId));
        return Result.Success();
    }

    public Result MarkFailed(string reason, Guid? actingUserId = null)
    {
        if (Status is TenantDomainStatus.Disabled)
            return Result.Failure(new Error("TenantDomain.Disabled", "Domain has been disabled."));

        Status = TenantDomainStatus.Failed;
        AddDomainEvent(new TenantDomainProvisioningFailed(TenantId, Id, Host, reason, actingUserId));
        return Result.Success();
    }

    /// <summary>
    /// Renombra el subdominio primario del tenant (Fase A7, §11 del plan v2). Solo aplica
    /// a DomainType.Subdomain: un custom hostname no se "renombra" — se deshabilita y se
    /// da de alta uno nuevo, porque implica volver a provisionar en Cloudflare. La
    /// disponibilidad del nuevo slug (unicidad, reservas activas) la valida el handler
    /// ANTES de llamar acá, igual que en CreateSubdomain.
    /// </summary>
    public Result ChangeSubdomain(SubdomainSlug newSlug, string baseDomain, Guid? actingUserId = null)
    {
        if (DomainType != TenantDomainType.Subdomain)
            return Result.Failure(new Error("TenantDomain.NotSubdomain", "Only platform subdomains can be renamed."));

        if (Status is TenantDomainStatus.Disabled)
            return Result.Failure(new Error("TenantDomain.Disabled", "Domain has been disabled."));

        var oldHost = Host;
        var newHost = $"{newSlug.Value}.{baseDomain.Trim().ToLowerInvariant()}";
        if (newHost == oldHost)
            return Result.Failure(
                new Error("TenantDomain.SlugUnchanged", "The new subdomain is the same as the current one.")
            );

        SubdomainSlug = newSlug.Value;
        Host = newHost;
        AddDomainEvent(
            new TenantSubdomainChanged(TenantId, Id, oldHost, newHost, NormalizeActor(actingUserId ?? Guid.Empty))
        );
        return Result.Success();
    }

    /// <summary>Deshabilita el dominio (desprovisioning) — no lo borra, queda en el historial/auditoría.</summary>
    public Result Disable(Guid? actingUserId = null)
    {
        if (IsPrimary)
        {
            return Result.Failure(
                new Error("TenantDomain.PrimaryCannotBeDisabled", "The primary domain cannot be disabled directly.")
            );
        }

        Status = TenantDomainStatus.Disabled;
        AddDomainEvent(new TenantDomainDisabled(TenantId, Id, Host, actingUserId));
        return Result.Success();
    }

    private static Guid? NormalizeActor(Guid userId) => userId == Guid.Empty ? null : userId;
}
