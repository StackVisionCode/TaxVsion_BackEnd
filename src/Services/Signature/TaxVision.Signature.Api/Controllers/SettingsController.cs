using BuildingBlocks.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Signature.Api.Authorization;
using TaxVision.Signature.Api.Common;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Settings;

namespace TaxVision.Signature.Api.Controllers;

/// <summary>
/// Configuración por tenant del microservicio Signature. Sólo lectura en Fase 0; los
/// endpoints de mutación llegan en fases posteriores conforme se definen los canales
/// avanzados (WhatsApp, App, KBA) y los flujos de rotación de claves.
/// </summary>
[ApiController]
[Route("signature/settings")]
public sealed class SettingsController(ITenantSignatureSettingsRepository repository) : ControllerBase
{
    /// <summary>
    /// Devuelve la configuración vigente del tenant del solicitante. El TenantId se
    /// toma exclusivamente del JWT (aislamiento multitenant).
    /// </summary>
    [HttpGet]
    [HasPermission(SignaturePermissions.SettingsManage)]
    [ProducesResponseType(typeof(TenantSignatureSettingsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var settings = await repository.GetByTenantIdAsync(tenantId, ct);
        if (settings is null)
            return NotFound();

        return Ok(TenantSignatureSettingsResponse.From(settings));
    }
}
