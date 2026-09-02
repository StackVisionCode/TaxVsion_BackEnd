using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Signature.Api.Requests;
using TaxVision.Signature.Application.Sealing;
using TaxVision.Signature.Application.Settings.Commands.ApplyPlanConstraints;
using TaxVision.Signature.Domain.Settings;
using Wolverine;

namespace TaxVision.Signature.Api.Controllers;

/// <summary>
/// Endpoints exclusivos de la plataforma (PlatformAdmin). No se exponen al SDK de tenant.
/// Requieren el permiso <c>signature.constraints.manage</c> en el JWT.
/// </summary>
[ApiController]
[Route("admin/tenants/{tenantId:guid}")]
[AllowActorTypes(ActorType.PlatformAdmin)]
public sealed class SignatureAdminController(IMessageBus bus) : ControllerBase
{
    /// <summary>
    /// Establece los techos de plan para un tenant específico.
    /// La configuración existente del tenant se auto-corrige si excede los nuevos límites.
    /// </summary>
    [HttpPut("signature-constraints")]
    [HasPermission(SignaturePermissions.PlanConstraintsManage)]
    [RateLimit("signature.g.admin_constraints_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateConstraints(
        Guid tenantId,
        [FromBody] UpdateSignaturePlanConstraintsBody body,
        CancellationToken ct
    )
    {
        // RBAC Fase 2: el chequeo defensivo inline que vivía acá ("TenantAdmin recibe el mismo
        // set de permisos que PlatformAdmin por defecto") ya no hace falta — SignaturePlanConstraintsManage
        // es PlatformOnly (nunca en SystemRoleDefaults(SystemTenantAdmin)) y esta acción además
        // requiere [AllowActorTypes(ActorType.PlatformAdmin)] a nivel de clase, así que
        // el tenantId de la ruta nunca puede ser manipulado por un TenantAdmin: [HasPermission] +
        // [AllowActorTypes] ya son 2 capas independientes que lo bloquean.
        if (!User.TryGetUserId(out var adminUserId))
            return Unauthorized();

        VerificationChannel allowedMask = VerificationChannel.None;
        foreach (var name in body.AllowedChannels)
        {
            if (
                !Enum.TryParse<VerificationChannel>(name, ignoreCase: true, out var ch)
                || ch == VerificationChannel.None
            )
                return BadRequest(
                    new Error("Signature.Constraints.InvalidChannel", $"Unknown verification channel: '{name}'.")
                );
            allowedMask |= ch;
        }

        if (allowedMask == VerificationChannel.None)
            return BadRequest(
                new Error("Signature.Constraints.NoChannels", "At least one verification channel must be specified.")
            );

        var cmd = new ApplyPlanConstraintsCommand(
            tenantId,
            adminUserId,
            body.MaxAllowedPdfBytes,
            body.MaxAllowedImageBytes,
            body.MaxAllowedPages,
            body.MinRetentionYears,
            body.PurgeAllowed,
            allowedMask,
            body.MaxTokenExpirationHours
        );

        var result = await bus.InvokeAsync<Result>(cmd, ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>
    /// Migración (una sola vez): re-asigna en CloudStorage los documentos firmados existentes del
    /// tenant al cliente firmante (antes se guardaban como OwnerType=Signature y no aparecían bajo el
    /// cliente en Documents). <c>dryRun=true</c> (default) solo cuenta; <c>dryRun=false</c> publica.
    /// Idempotente. Los sellados NUEVOS ya salen bien del flujo de firma.
    /// </summary>
    // Ruta ABSOLUTA (fuera del prefijo admin/tenants/{tenantId} de la clase) para que sea
    // alcanzable por el gateway, que solo enruta /signature/**. tenantId va por query.
    [HttpPost("/signature/admin/reassign-sealed-owners")]
    [HasPermission(SignaturePermissions.PlanConstraintsManage)]
    [RateLimit("signature.g.admin_constraints_manage")]
    [ProducesResponseType<ReassignedSealedOwnersReport>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ReassignSealedOwners(
        [FromQuery] Guid tenantId,
        [FromQuery] bool dryRun = true,
        CancellationToken ct = default
    )
    {
        var result = await bus.InvokeAsync<Result<ReassignedSealedOwnersReport>>(
            new ReassignSealedDocumentOwnersCommand(tenantId, dryRun),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
