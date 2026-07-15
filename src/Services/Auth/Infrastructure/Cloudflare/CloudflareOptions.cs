namespace TaxVision.Auth.Infrastructure.Cloudflare;

/// <summary>
/// Credenciales de Cloudflare for SaaS (Fase A5). El API Token debe estar scoped a
/// Zone-&gt;DNS-&gt;Edit + Zone-&gt;SSL and Certificates-&gt;Edit sobre la zona de
/// ZoneId únicamente — nunca la Global API Key (ver Config/cloudflare-prod.md §4).
/// </summary>
public sealed class CloudflareOptions
{
    public const string SectionName = "Cloudflare";

    public string BaseUrl { get; set; } = "https://api.cloudflare.com/client/v4/";

    public string ApiToken { get; set; } = string.Empty;

    public string ZoneId { get; set; } = string.Empty;
}
