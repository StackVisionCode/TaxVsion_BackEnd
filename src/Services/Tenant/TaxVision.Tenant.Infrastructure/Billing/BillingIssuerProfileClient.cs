using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.Tenant.Application.Profiles.Abstractions;
using TaxVision.Tenant.Infrastructure.Branding;

namespace TaxVision.Tenant.Infrastructure.Billing;

/// <summary>
/// Lee <c>GET internal/billing/issuer-profiles?page=&amp;size=</c> por M2M. Solo lo usa la importación
/// única de perfiles: cualquier fallo (sin token, Billing caído, 4xx/5xx, cuerpo ilegible) corta la
/// importación en vez de tomarlo como "no hay más".
/// </summary>
internal sealed class BillingIssuerProfileClient(
    HttpClient http,
    ITenantServiceTokenAcquirer tokenAcquirer,
    ILogger<BillingIssuerProfileClient> logger
) : IBillingIssuerProfileClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<Result<BillingIssuerProfilePage>> GetPageAsync(int page, int size, CancellationToken ct = default)
    {
        // El listado cruza todas las oficinas: el token se pide en nombre del tenant de plataforma.
        var token = await tokenAcquirer.GetTokenAsync(PlatformTenant.Id, ct);
        if (string.IsNullOrWhiteSpace(token))
            return Result.Failure<BillingIssuerProfilePage>(BillingIssuerProfileErrors.Unavailable);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"internal/billing/issuer-profiles?page={page}&size={size}"
        );
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Billing issuer profiles page {Page} responded {StatusCode}.",
                    page,
                    (int)response.StatusCode
                );
                return Result.Failure<BillingIssuerProfilePage>(BillingIssuerProfileErrors.Unavailable);
            }

            var body = await response.Content.ReadFromJsonAsync<BillingIssuerProfilePage>(Json, ct);
            return body is null
                ? Result.Failure<BillingIssuerProfilePage>(BillingIssuerProfileErrors.Unavailable)
                : Result.Success(body with { Items = body.Items ?? [] });
        }
        catch (Exception ex)
            when (ex is HttpRequestException or JsonException
                || (ex is TaskCanceledException && !ct.IsCancellationRequested)
            )
        {
            logger.LogWarning(ex, "Could not read Billing issuer profiles page {Page}.", page);
            return Result.Failure<BillingIssuerProfilePage>(BillingIssuerProfileErrors.Unavailable);
        }
    }
}
