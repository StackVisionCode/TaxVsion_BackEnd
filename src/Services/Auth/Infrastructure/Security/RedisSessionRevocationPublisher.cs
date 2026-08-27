using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Infrastructure.Security;

/// <summary>
/// Publica en el canal Redis que escucha Communication. El payload va en camelCase y los GUID CON
/// guiones (mismo formato que los claims sub/tenant_id/sid del JWT): Communication arma el room del
/// socket como <c>t:{tenantId}:u:{userId}</c> con esos claims, y cualquier otro formato no casaría el
/// room y el push se perdería. No lanza si Redis falla — el aviso en tiempo real es una mejora sobre
/// la denylist, no la fuente de verdad.
/// </summary>
public sealed class RedisSessionRevocationPublisher(
    IConnectionMultiplexer redis,
    ILogger<RedisSessionRevocationPublisher> logger
) : ISessionRevocationPublisher
{
    private static readonly RedisChannel Channel = RedisChannel.Literal("auth:session-revoked");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task PublishRevokedAsync(
        Guid tenantId,
        Guid userId,
        Guid sessionId,
        string reason,
        CancellationToken ct = default
    )
    {
        try
        {
            var payload = JsonSerializer.Serialize(
                new
                {
                    tenantId = tenantId.ToString(),
                    userId = userId.ToString(),
                    sessionId = sessionId.ToString(),
                    jti = (string?)null,
                    reason,
                    revokedAtUtc = DateTime.UtcNow.ToString("O"),
                },
                Json
            );
            await redis.GetSubscriber().PublishAsync(Channel, payload);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "No se pudo publicar la revocación de la sesión {SessionId}; el logout en tiempo real se omite (la denylist sigue vigente)",
                sessionId
            );
        }
    }
}
