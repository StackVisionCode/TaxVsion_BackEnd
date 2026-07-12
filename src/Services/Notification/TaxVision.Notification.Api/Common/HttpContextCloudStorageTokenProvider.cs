using Microsoft.AspNetCore.Http;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Api.Common;

/// <summary>
/// Provee el bearer token para llamar a CloudStorage. En contexto de request HTTP reenvía el token del
/// usuario (operaciones de plantillas/layouts iniciadas por un usuario). En contexto background
/// (worker de sincronización) obtiene un token de servicio (M2M) del tenant via <see cref="IServiceTokenAcquirer"/>.
/// </summary>
public sealed class CloudStorageTokenProvider(IHttpContextAccessor accessor, IServiceTokenAcquirer serviceTokens)
    : ICloudStorageTokenProvider
{
    public async Task<string?> GetTokenAsync(Guid? tenantId, CancellationToken ct = default)
    {
        var header = accessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(header))
        {
            const string prefix = "Bearer ";
            return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? header[prefix.Length..] : header;
        }

        // Sin request de usuario: token de servicio para el tenant (subida de adjuntos sincronizados, etc.).
        if (tenantId is { } tenant && tenant != Guid.Empty)
            return await serviceTokens.GetTokenAsync(tenant, ct);

        return null;
    }
}
