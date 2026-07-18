using System.Net.Http.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Infrastructure.Cloudflare;

/// <summary>
/// Cliente de la API de Cloudflare for SaaS para custom hostnames (Fase A5). El
/// HttpClient se configura en DI con BaseAddress + el API Token scoped como Bearer
/// (ver DependencyInjection.AddAuthInfrastructure). Status/SslStatus viajan como
/// strings opacos tal como los devuelve Cloudflare — no se re-mapean a un enum
/// propio para no tener que perseguir cada valor nuevo que Cloudflare agregue.
/// </summary>
public sealed class CloudflareProvisioningClient(
    HttpClient httpClient,
    IOptions<CloudflareOptions> options,
    ILogger<CloudflareProvisioningClient> logger
) : ICloudflareProvisioningClient
{
    public async Task<Result<CustomHostnameResult>> CreateCustomHostnameAsync(
        string hostname,
        CancellationToken ct = default
    )
    {
        var body = new CreateCustomHostnameRequest(
            hostname,
            new SslSettingsRequest("http", "dv", new SslSettingsDetailRequest("1.2", "on", "on"))
        );
        var response = await httpClient.PostAsJsonAsync($"zones/{options.Value.ZoneId}/custom_hostnames", body, ct);
        return await ParseAsync(response, "create", ct);
    }

    public async Task<Result<CustomHostnameResult>> GetCustomHostnameAsync(
        string cloudflareId,
        CancellationToken ct = default
    )
    {
        var response = await httpClient.GetAsync($"zones/{options.Value.ZoneId}/custom_hostnames/{cloudflareId}", ct);
        return await ParseAsync(response, "get", ct);
    }

    public async Task<Result> DeleteCustomHostnameAsync(string cloudflareId, CancellationToken ct = default)
    {
        var response = await httpClient.DeleteAsync(
            $"zones/{options.Value.ZoneId}/custom_hostnames/{cloudflareId}",
            ct
        );
        if (response.IsSuccessStatusCode)
            return Result.Success();

        logger.LogWarning(
            "Cloudflare delete custom hostname {CloudflareId} returned HTTP {Status}.",
            cloudflareId,
            (int)response.StatusCode
        );
        return Result.Failure(
            new Error("TenantDomain.CloudflareHttp", $"Cloudflare HTTP status {(int)response.StatusCode}.")
        );
    }

    private async Task<Result<CustomHostnameResult>> ParseAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken ct
    )
    {
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Cloudflare {Operation} custom hostname returned HTTP {Status}.",
                operation,
                (int)response.StatusCode
            );
            return Result.Failure<CustomHostnameResult>(
                new Error("TenantDomain.CloudflareHttp", $"Cloudflare HTTP status {(int)response.StatusCode}.")
            );
        }

        var envelope = await response.Content.ReadFromJsonAsync<CloudflareEnvelope>(ct);
        if (envelope is null || !envelope.Success || envelope.Result is null)
        {
            var reason = envelope?.Errors?.FirstOrDefault()?.Message ?? "empty response";
            logger.LogWarning("Cloudflare {Operation} custom hostname rejected: {Reason}.", operation, reason);
            return Result.Failure<CustomHostnameResult>(new Error("TenantDomain.CloudflareRejected", reason));
        }

        return Result.Success(ToResult(envelope.Result));
    }

    private static CustomHostnameResult ToResult(CloudflareCustomHostname hostname)
    {
        var dcvRecords =
            hostname
                .Ssl.ValidationRecords?.Select(record =>
                    record.TxtName is not null
                        ? $"TXT {record.TxtName} = {record.TxtValue}"
                        : $"HTTP {record.HttpUrl} -> {record.HttpBody}"
                )
                .ToList()
            ?? [];

        return new CustomHostnameResult(
            hostname.Id,
            hostname.Status,
            hostname.Ssl.Status,
            hostname.OwnershipVerification?.Name,
            hostname.OwnershipVerification?.Value,
            dcvRecords
        );
    }

    private sealed record CreateCustomHostnameRequest(string Hostname, SslSettingsRequest Ssl);

    private sealed record SslSettingsRequest(string Method, string Type, SslSettingsDetailRequest Settings);

    private sealed record SslSettingsDetailRequest(
        [property: JsonPropertyName("min_tls_version")] string MinTlsVersion,
        [property: JsonPropertyName("tls_1_3")] string Tls13,
        string Http2
    );

    private sealed record CloudflareEnvelope(
        bool Success,
        CloudflareCustomHostname? Result,
        IReadOnlyList<CloudflareApiError>? Errors
    );

    private sealed record CloudflareApiError(int Code, string Message);

    private sealed record CloudflareCustomHostname(
        string Id,
        string Status,
        CloudflareSsl Ssl,
        [property: JsonPropertyName("ownership_verification")] CloudflareOwnershipVerification? OwnershipVerification
    );

    private sealed record CloudflareSsl(
        string Status,
        [property: JsonPropertyName("validation_records")] IReadOnlyList<CloudflareValidationRecord>? ValidationRecords
    );

    private sealed record CloudflareValidationRecord(
        [property: JsonPropertyName("txt_name")] string? TxtName,
        [property: JsonPropertyName("txt_value")] string? TxtValue,
        [property: JsonPropertyName("http_url")] string? HttpUrl,
        [property: JsonPropertyName("http_body")] string? HttpBody
    );

    private sealed record CloudflareOwnershipVerification(string Type, string Name, string Value);
}
