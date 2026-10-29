using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Security;

// Auth limita by-host por IP de quien llama y todas las llamadas salen de este servicio: sin cache,
// el tráfico público de toda la plataforma comparte un solo cupo. Ante cualquier fallo se cierra.
public sealed class FormPublicTenantHostValidator(
    HttpClient httpClient,
    IMemoryCache cache,
    ILogger<FormPublicTenantHostValidator> logger,
    IOptions<ClientFormsOptions> options,
    IHostEnvironment environment
) : IFormPublicTenantHostValidator
{
    public static readonly TimeSpan ResolvedTtl = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan UnresolvedTtl = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan FailureTtl = TimeSpan.FromSeconds(10);

    public async Task<bool> MatchesTenantAsync(Guid tenantId, string host, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(host))
            return false;
        var normalized = host.Trim().ToLowerInvariant();
        // En local el navegador manda Origin localhost, que Auth no resuelve a ninguna oficina.
        if (
            environment.IsDevelopment()
            && options.Value.External.DevelopmentHosts.Contains(normalized, StringComparer.OrdinalIgnoreCase)
        )
            return true;
        var key = $"cf-public-host:{normalized}";
        if (!cache.TryGetValue(key, out Guid? resolved))
        {
            var lookup = await ResolveAsync(normalized, ct);
            resolved = lookup.TenantId;
            cache.Set(
                key,
                resolved,
                new MemoryCacheEntryOptions
                {
                    // IMemoryCache es compartido: sin Size revienta si otro consumidor fijó SizeLimit.
                    Size = 1,
                    AbsoluteExpirationRelativeToNow = lookup.Ttl,
                }
            );
        }
        return resolved == tenantId;
    }

    private async Task<(Guid? TenantId, TimeSpan Ttl)> ResolveAsync(string host, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "auth/tenant-resolution/by-host");
            request.Headers.Host = host;
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return (null, UnresolvedTtl);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Public form host resolution failed with status {StatusCode}; request rejected.",
                    (int)response.StatusCode
                );
                return (null, FailureTtl);
            }
            var resolved = await response.Content.ReadFromJsonAsync<TenantResolutionResponse>(cancellationToken: ct);
            return resolved is null || resolved.TenantId == Guid.Empty
                ? (null, UnresolvedTtl)
                : (resolved.TenantId, ResolvedTtl);
        }
        catch (Exception ex) when (IsTransient(ex, ct))
        {
            logger.LogWarning(ex, "Public form host resolution is unavailable; request rejected.");
            return (null, FailureTtl);
        }
    }

    private static bool IsTransient(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException or FormatException or JsonException or NotSupportedException
        || (ex is OperationCanceledException && !ct.IsCancellationRequested);

    private sealed record TenantResolutionResponse(Guid TenantId, string Name, string Status);
}
