using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Signature.Api.Common;
using TaxVision.Signature.Application.Documents.Commands.Validate;
using Wolverine;

namespace TaxVision.Signature.Api.Controllers;

/// <summary>
/// Endpoints previos a la creación de una <c>SignatureRequest</c>. El más importante
/// es <c>POST /signature/documents/validate</c> — preflight que valida MIME, tamaño,
/// integridad estructural, número de páginas y firmas previas. Nunca acepta un PDF
/// sin pasar por aquí (regla P-04 del diseño).
/// </summary>
[ApiController]
[Route("signature/documents")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class SignatureDocumentsController(IMessageBus bus) : ControllerBase
{
    /// <summary>Tamaño máximo aceptado en el body (25 MB — igual que el validator).</summary>
    private const long MaxRequestBytes = 25L * 1024 * 1024;

    // ---------- POST /signature/documents/validate ----------
    [HttpPost("validate")]
    [HasPermission(SignaturePermissions.RequestCreate)]
    [RequestSizeLimit(MaxRequestBytes)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<ValidateDocumentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Validate([FromForm] IFormFile file, CancellationToken ct)
    {
        if (!TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        if (file is null || file.Length == 0)
            return BadRequest(new Error("Signature.DocumentValidation.NoFile", "A file is required."));

        var content = await ReadAllBytesAsync(file, ct);
        var cmd = new ValidateDocumentCommand(tenantId, userId, content, file.FileName, file.ContentType);
        var result = await bus.InvokeAsync<Result<ValidateDocumentResponse>>(cmd, ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ------------------------------------------------------------------
    // Helpers privados: una responsabilidad por método
    // ------------------------------------------------------------------

    private static async Task<byte[]> ReadAllBytesAsync(IFormFile file, CancellationToken ct)
    {
        using var stream = new MemoryStream((int)file.Length);
        await file.CopyToAsync(stream, ct);
        return stream.ToArray();
    }

    private bool TryGetTenantAndUser(out Guid tenantId, out Guid userId)
    {
        if (!User.TryGetTenantId(out tenantId))
        {
            userId = Guid.Empty;
            return false;
        }
        return User.TryGetUserId(out userId);
    }
}
