using System.IO.Compression;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Sharing;
using Wolverine;

namespace TaxVision.CloudStorage.Api.Controllers;

/// <summary>
/// Fase C3 — endpoint PUBLICO de resolucion de token. Sin [Authorize], rate
/// limited por IP+ruta. Respuestas uniformes: nunca distingue "no existe" de
/// "revocado/expirado/agotado" (anti-enumeracion, ver ResolvePublicShareHandler).
/// </summary>
[ApiController]
[Route("storage")]
[AllowAnonymous]
[EnableRateLimiting("share-public")]
public sealed class PublicShareController(
    IMessageBus bus,
    IObjectStorage storage,
    IOptions<CloudStorageOptions> storageOptions
) : ControllerBase
{
    /// <summary>Fase C4 — fileId es obligatorio cuando el token es de un link de tipo Folder (ver FolderShareCoverage).</summary>
    [HttpGet("public/{token}")]
    [RateLimitExempt(
        "Endpoint anonimo (sin JWT) — TieredRateLimitEvaluator solo soporta particion por Tenant/User, "
            + "asi que [RateLimit] fallaria abierto aqui. La proteccion real la da el limiter nativo "
            + "[EnableRateLimiting(\"share-public\")] (IP+ruta, 20/min FixedWindow), que se deja intacto."
    )]
    public async Task<IActionResult> ResolvePublic(
        string token,
        [FromQuery] string? password,
        [FromQuery] string? email,
        [FromQuery] Guid? fileId,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<ShareAccessResult>(
            new ResolvePublicShareQuery(token, password, email, fileId, RemoteIp(), UserAgent()),
            ct
        );
        return result.Outcome switch
        {
            ShareAccessOutcome.Redirect => Redirect(result.PresignedUrl!),
            ShareAccessOutcome.PasswordRequired => StatusCode(
                StatusCodes.Status401Unauthorized,
                new { requiresPassword = true }
            ),
            _ => NotFound(),
        };
    }

    /// <summary>
    /// Descriptor no sensible para la landing pública (nombre/tamaño/permiso), sin servir el archivo.
    /// Si el link tiene contraseña, devuelve solo <c>requiresPassword</c> y omite el nombre. Token
    /// inválido/expirado/revocado → 404 uniforme (mismo anti-enumeración que ResolvePublic).
    /// </summary>
    [HttpGet("public/{token}/meta")]
    [RateLimitExempt(
        "Endpoint anonimo (sin JWT) — mismo criterio que ResolvePublic: lo protege el limiter nativo "
            + "[EnableRateLimiting(\"share-public\")] (IP+ruta), no [RateLimit] (que particiona por Tenant/User)."
    )]
    public async Task<IActionResult> ResolvePublicMeta(
        string token,
        [FromQuery] string? email,
        [FromQuery] Guid? fileId,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<ShareMetaResult>(new ResolvePublicShareMetaQuery(token, email, fileId), ct);
        return result.Outcome switch
        {
            ShareMetaOutcome.Available => Ok(
                new
                {
                    ready = true,
                    requiresPassword = false,
                    fileName = result.FileName,
                    sizeBytes = result.SizeBytes,
                    contentType = result.ContentType,
                    permission = result.Permission,
                    expiresAt = result.ExpiresAtUtc,
                }
            ),
            ShareMetaOutcome.PasswordRequired => Ok(new { ready = false, requiresPassword = true }),
            _ => NotFound(),
        };
    }

    /// <summary>8.2 — navega el contenido de una carpeta compartida (subcarpetas + archivos), sin servir binarios.</summary>
    [HttpGet("public/{token}/folder")]
    [RateLimitExempt("Endpoint anonimo — lo protege el limiter nativo share-public (IP+ruta), no el tiered.")]
    public async Task<IActionResult> ResolvePublicFolder(
        string token,
        [FromQuery] string? password,
        [FromQuery] string? email,
        [FromQuery] Guid? folderId,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<PublicFolderContentsResult>(
            new ResolvePublicFolderContentsQuery(token, password, email, folderId),
            ct
        );
        return result.Outcome switch
        {
            PublicFolderOutcome.Available => Ok(
                new
                {
                    folderId = result.FolderId,
                    folderName = result.FolderName,
                    isRecursive = result.IsRecursive,
                    permission = result.Permission,
                    expiresAt = result.ExpiresAtUtc,
                    breadcrumb = result.Breadcrumb,
                    subfolders = result.Subfolders,
                    files = result.Files,
                }
            ),
            PublicFolderOutcome.PasswordRequired => StatusCode(
                StatusCodes.Status401Unauthorized,
                new { requiresPassword = true }
            ),
            _ => NotFound(),
        };
    }

    /// <summary>8.2 — "Download all (ZIP)" de una carpeta compartida, streameado directo al Response.Body.</summary>
    [HttpGet("public/{token}/zip")]
    [EnableRateLimiting("share-public-zip")]
    [RateLimitExempt("Endpoint anonimo — lo protege el limiter nativo share-public-zip (6/min IP), no el tiered.")]
    public async Task<IActionResult> DownloadPublicFolderZip(
        string token,
        [FromQuery] string? password,
        [FromQuery] string? email,
        [FromQuery] Guid? folderId,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<PublicZipResult>(
            new PreparePublicFolderZipQuery(
                token,
                password,
                email,
                folderId,
                new RequestAuditContext(RemoteIp(), UserAgent(), token)
            ),
            ct
        );

        switch (result.Outcome)
        {
            case PublicZipOutcome.PasswordRequired:
                return StatusCode(StatusCodes.Status401Unauthorized, new { requiresPassword = true });
            case PublicZipOutcome.TooLarge:
                return StatusCode(
                    StatusCodes.Status413PayloadTooLarge,
                    new
                    {
                        code = "ShareLink.ZipTooLarge",
                        message = "This folder is too large to download as a single ZIP. Open it and download files individually.",
                    }
                );
            case PublicZipOutcome.Ready:
                break;
            default:
                return NotFound();
        }

        Response.ContentType = "application/zip";
        Response.Headers.ContentDisposition = $"attachment; filename=\"{result.ArchiveName}\"";
        // ZipArchive escribe el directorio central/data-descriptors de forma SÍNCRONA al cerrar; Kestrel
        // prohíbe IO síncrono por defecto (revienta con 500 a mitad del stream → "Network issue" en el
        // navegador). Se habilita solo para esta respuesta de streaming.
        HttpContext.Features.Get<IHttpBodyControlFeature>()!.AllowSynchronousIO = true;
        var mainBucket = storageOptions.Value.MainBucket;
        using (var zip = new ZipArchive(Response.Body, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in result.Plan!.Entries)
            {
                var zipEntry = zip.CreateEntry(entry.EntryName, CompressionLevel.Fastest);
                await using var entryStream = zipEntry.Open();
                await storage.DownloadAsync(mainBucket, entry.ObjectKey, entryStream, ct);
            }
        }
        return new EmptyResult();
    }

    private string? RemoteIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

    private string? UserAgent() => Request.Headers.UserAgent.ToString();
}
