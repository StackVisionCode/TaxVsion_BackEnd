using BuildingBlocks.Results;

namespace TaxVision.Auth.Application.Abstractions;

/// <summary>
/// Estado de un custom hostname reportado por Cloudflare. Status/SslStatus se tratan
/// como strings opacos (ver Config/cloudflare-prod.md §3) — no VO: son un espejo
/// fiel de lo que Cloudflare devuelve, sin invariante propio que este servicio deba
/// validar más allá de "está o no en active/active".
/// </summary>
public sealed record CustomHostnameResult(
    string CloudflareId,
    string Status,
    string SslStatus,
    string? OwnershipTxtName,
    string? OwnershipTxtValue,
    IReadOnlyList<string> DcvRecords
)
{
    public bool IsFullyActive => Status == "active" && SslStatus == "active";

    public bool IsBlocked => Status == "blocked";
}

/// <summary>
/// Fase A5 — Cloudflare for SaaS (custom hostnames de dominio propio del tenant).
/// Los subdominios *.taxprocore.com nunca pasan por aquí: el wildcard DNS ya los
/// cubre, así que TenantDomain.CreateSubdomain arranca directo en Active (Fase A2).
/// </summary>
public interface ICloudflareProvisioningClient
{
    Task<Result<CustomHostnameResult>> CreateCustomHostnameAsync(string hostname, CancellationToken ct = default);

    Task<Result<CustomHostnameResult>> GetCustomHostnameAsync(string cloudflareId, CancellationToken ct = default);

    /// <summary>Anti subdomain-takeover: se llama siempre al deshabilitar un custom hostname.</summary>
    Task<Result> DeleteCustomHostnameAsync(string cloudflareId, CancellationToken ct = default);
}
