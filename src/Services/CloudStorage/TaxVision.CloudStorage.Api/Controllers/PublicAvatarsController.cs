using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Files.Avatars;

namespace TaxVision.CloudStorage.Api.Controllers;

/// <summary>
/// Foto de perfil por fileId, sin token: un &lt;img src&gt; nunca lleva el JWT y la URL firmada de 5 min
/// obligaría a pedirla (y auditarla) en cada lista. El fileId es la llave (GUID aleatorio) y SOLO se
/// sirven archivos Avatars disponibles; cualquier otro caso es el mismo 404 (sin señal de enumeración).
/// </summary>
[ApiController]
[Route("storage/avatars")]
[AllowAnonymous]
[EnableRateLimiting(AvatarRateLimitPolicy)]
public sealed class PublicAvatarsController(
    PublicAvatarReader avatars,
    IObjectStorage storage,
    IOptions<CloudStorageOptions> storageOptions,
    ILogger<PublicAvatarsController> logger
) : ControllerBase
{
    public const string AvatarRateLimitPolicy = "avatar-public";

    // El contenido de un fileId nunca cambia (foto nueva = fileId nuevo). private: es un dato personal,
    // no debe quedar en la caché compartida de Cloudflare.
    internal const string CacheControlValue = "private, max-age=31536000, immutable";

    [HttpGet("{fileId:guid}")]
    [RateLimitExempt(
        "Endpoint anonimo (sin JWT) — el evaluador tiered particiona por Tenant/User y fallaria abierto. "
            + "Lo protege el limiter nativo [EnableRateLimiting(\"avatar-public\")] por IP."
    )]
    public async Task<IActionResult> Get(Guid fileId, CancellationToken ct)
    {
        // Sin fila de auditoría ni FileAccessAudited por petición: es una imagen pública que aparece en
        // listas de decenas de personas por pantalla; auditarla inundaría el log sin valor de seguridad.
        var avatar = await avatars.ResolveAsync(fileId, ct);
        if (avatar is null)
            return NotFoundUncached();

        var etag = new EntityTagHeaderValue($"\"{avatar.Sha256}\"");
        if (MatchesIfNoneMatch(etag))
        {
            ApplyCacheHeaders(etag);
            return StatusCode(StatusCodes.Status304NotModified);
        }

        byte[] bytes;
        try
        {
            using var buffer = new MemoryStream(capacity: (int)Math.Clamp(avatar.SizeBytes, 0, 8 * 1024 * 1024));
            await storage.DownloadAsync(storageOptions.Value.MainBucket, avatar.ObjectKey, buffer, ct);
            bytes = buffer.ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // La caché pudo tener un avatar ya purgado: se revalida contra la BD antes de decidir.
            if (await avatars.RevalidateAsync(fileId, ct) is null)
                return NotFoundUncached();
            logger.LogError(ex, "Avatar {FileId} is servable but its object could not be read.", fileId);
            throw;
        }

        ApplyCacheHeaders(etag);
        Response.Headers[HeaderNames.ContentDisposition] = "inline";
        return File(bytes, avatar.ContentType);
    }

    private bool MatchesIfNoneMatch(EntityTagHeaderValue etag)
    {
        var ifNoneMatch = Request.GetTypedHeaders().IfNoneMatch;
        return ifNoneMatch is { Count: > 0 }
            && ifNoneMatch.Any(candidate =>
                candidate.Equals(EntityTagHeaderValue.Any) || candidate.Compare(etag, useStrongComparison: false)
            );
    }

    private void ApplyCacheHeaders(EntityTagHeaderValue etag)
    {
        var headers = Response.Headers;
        headers[HeaderNames.CacheControl] = CacheControlValue;
        headers[HeaderNames.ETag] = etag.ToString();
        headers[HeaderNames.XContentTypeOptions] = "nosniff";
        headers["Cross-Origin-Resource-Policy"] = "same-site";
    }

    // Un 404 no se cachea: el fileId puede volverse servible en segundos (avatar aún en escaneo).
    private NotFoundResult NotFoundUncached()
    {
        Response.Headers[HeaderNames.CacheControl] = "no-store";
        return NotFound();
    }
}
