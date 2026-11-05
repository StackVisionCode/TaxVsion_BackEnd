using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Signature.Api.Requests;
using TaxVision.Signature.Application.Admin;
using TaxVision.Signature.Application.Audit;
using TaxVision.Signature.Application.Delivery;
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
    /// Fija la excepción del tenant sobre su plan (campo nulo = hereda del plan). Lo que eligió la oficina
    /// por encima de los techos nuevos se acota al leerlo.
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

        VerificationChannel? allowedChannels = null;
        if (body.AllowedChannels is { } channelNames)
        {
            var allowedMask = VerificationChannel.None;
            foreach (var name in channelNames)
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
                    new Error(
                        "Signature.Constraints.NoChannels",
                        "At least one verification channel must be specified."
                    )
                );
            allowedChannels = allowedMask;
        }

        var cmd = new ApplyPlanConstraintsCommand(
            tenantId,
            adminUserId,
            body.MaxDocumentsPerRequest,
            body.MaxSignersPerRequest,
            body.MaxAllowedPdfBytes,
            body.MaxAllowedPages,
            body.MinRetentionYears,
            body.PurgeAllowed,
            allowedChannels,
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

    /// <summary>
    /// Migración (una sola vez): las solicitudes de VARIOS clientes firmantes completadas antes de que
    /// existieran las copias de portal no las tienen. Genera la copia de cada documento firmado (y sus
    /// certificados) en la carpeta "Signed Documents" de cada cliente. <c>dryRun=true</c> (default) solo
    /// cuenta. Idempotente: se puede repetir; las que no estaban disponibles quedan para otra pasada.
    /// </summary>
    [HttpPost("/signature/admin/backfill-portal-copies")]
    [HasPermission(SignaturePermissions.PlanConstraintsManage)]
    [RateLimit("signature.g.admin_constraints_manage")]
    [ProducesResponseType<PortalCopiesBackfillReport>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> BackfillPortalCopies(
        [FromQuery] Guid tenantId,
        [FromQuery] bool dryRun = true,
        CancellationToken ct = default
    )
    {
        var result = await bus.InvokeAsync<Result<PortalCopiesBackfillReport>>(
            new BackfillCustomerPortalCopiesCommand(tenantId, dryRun),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>
    /// Reposición SIG-1: completa una solicitud con todos los firmantes firmados que quedó sin completar y
    /// publica lo mismo que el camino normal. Idempotente. Las <c>Expired</c> solo con <c>allowExpired=true</c>
    /// (D-SIG-1). <c>dryRun=true</c> (default) solo valida.
    /// </summary>
    [HttpPost("/signature/admin/requests/{requestId:guid}/reconcile-completion")]
    [HasPermission(SignaturePermissions.PlanConstraintsManage)]
    [RateLimit("signature.g.admin_constraints_manage")]
    [ProducesResponseType<ReconcileCompletionReport>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ReconcileCompletion(
        Guid requestId,
        [FromQuery] Guid tenantId,
        [FromQuery] bool allowExpired = false,
        [FromQuery] bool dryRun = true,
        CancellationToken ct = default
    )
    {
        var result = await bus.InvokeAsync<Result<ReconcileCompletionReport>>(
            new ReconcileCompletionCommand(tenantId, requestId, allowExpired, dryRun),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>
    /// SIG-2: vuelve a pedir el sellado de una solicitud completada (después de corregir un fallo permanente).
    /// Limpia la marca de fallo. Idempotente: lo ya sellado se salta.
    /// </summary>
    [HttpPost("/signature/admin/requests/{requestId:guid}/reseal")]
    [HasPermission(SignaturePermissions.PlanConstraintsManage)]
    [RateLimit("signature.g.admin_constraints_manage")]
    [ProducesResponseType<ResealReport>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Reseal(Guid requestId, [FromQuery] Guid tenantId, CancellationToken ct = default)
    {
        var result = await bus.InvokeAsync<Result<ResealReport>>(
            new ResealSignatureRequestCommand(tenantId, requestId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>
    /// Reposición SIG-2 (D-SIG-2): agrega la línea de auditoría <c>RequestSealed</c> que le falta a los documentos
    /// ya sellados, sin publicar avisos. Sin <c>tenantId</c>, todas las oficinas. <c>dryRun=true</c> (default)
    /// solo cuenta. Idempotente.
    /// </summary>
    [HttpPost("/signature/admin/backfill-sealed-audit-lines")]
    [HasPermission(SignaturePermissions.PlanConstraintsManage)]
    [RateLimit("signature.g.admin_constraints_manage")]
    [ProducesResponseType<SealedAuditBackfillReport>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> BackfillSealedAuditLines(
        [FromQuery] Guid? tenantId,
        [FromQuery] bool dryRun = true,
        CancellationToken ct = default
    )
    {
        var result = await bus.InvokeAsync<Result<SealedAuditBackfillReport>>(
            new BackfillSealedAuditLinesCommand(tenantId, dryRun),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
