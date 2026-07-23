using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Signature.Api.Requests;
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
}
