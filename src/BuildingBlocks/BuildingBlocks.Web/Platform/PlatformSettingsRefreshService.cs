using System.Net.Http.Json;
using BuildingBlocks.Platform;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Web.Platform;

/// <summary>
/// Lee la versión de Tenant al arrancar y después cada <see cref="PlatformSettingsOptions.RefreshInterval"/>.
/// Es la garantía detrás del evento: un servicio que estuvo caído cuando cambió la marca, o una
/// réplica que no recibió el mensaje, termina con la versión correcta igual.
/// </summary>
public sealed class PlatformSettingsRefreshService(
    IHttpClientFactory httpClientFactory,
    PlatformSettingsProvider provider,
    IOptions<PlatformSettingsOptions> options,
    ILogger<PlatformSettingsRefreshService> logger
) : BackgroundService
{
    public const string HttpClientName = "platform-settings";
    public const string PublicPath = "platform/settings/public";

    private readonly PlatformSettingsOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.TenantBaseUrl))
        {
            logger.LogInformation(
                "PlatformSettings:TenantBaseUrl is not set; using local platform settings (brand={BrandName}).",
                provider.Current.BrandName
            );
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var refreshed = await RefreshOnceAsync(stoppingToken);
            var delay = refreshed ? _options.RefreshInterval : _options.RetryInterval;

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Una lectura. Devuelve si Tenant contestó (aunque la versión ya fuera la vigente).</summary>
    public async Task<bool> RefreshOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            var baseUrl = _options.TenantBaseUrl!.TrimEnd('/');
            var body = await client.GetFromJsonAsync<PublicPlatformSettings>(
                $"{baseUrl}/{PublicPath}",
                cancellationToken
            );
            if (body is null)
                return false;

            if (provider.TryApply(body.ToSnapshot()))
            {
                logger.LogInformation(
                    "Platform settings v{Version} applied from Tenant (brand={BrandName}, domain={BaseDomain}).",
                    body.Version,
                    body.BrandName,
                    body.BaseDomain
                );
            }

            return true;
        }
        catch (Exception ex)
            when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            if (cancellationToken.IsCancellationRequested)
                return false;

            logger.LogWarning(
                "Could not read platform settings from Tenant ({Error}); keeping v{Version}.",
                ex.Message,
                provider.Current.Version
            );
            return false;
        }
    }

    private sealed record PublicPlatformSettings(
        long Version,
        string BrandName,
        string LegalName,
        string BaseDomain,
        string ApiHost,
        string SupportEmail,
        string BillingEmail
    )
    {
        public PlatformSettingsSnapshot ToSnapshot() =>
            new(Version, BrandName, LegalName, BaseDomain, ApiHost, SupportEmail, BillingEmail);
    }
}
