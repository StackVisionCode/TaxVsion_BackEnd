using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Correspondence.Application.Ingest;
using Wolverine;

namespace TaxVision.Correspondence.Api.Controllers;

/// <summary>
/// Reposiciones de plataforma de Correspondence. Las llama un PlatformAdmin (por el Gateway) o un token de servicio
/// emitido para la PlatformTenant (M2M, contenedor a contenedor). Nunca un usuario de oficina.
/// </summary>
[ApiController]
[AllowActorTypes(ActorType.PlatformAdmin, ActorType.Service)]
public sealed class CorrespondenceAdminController(IMessageBus bus) : ControllerBase
{
    /// <summary>
    /// Reposición COR-02: saca de cada hilo los correos de otro cliente (consulta Q-COR-3). <c>dryRun=true</c> (por
    /// defecto) solo cuenta. Con <c>dryRun=false</c> repara un hilo por transacción y reporta los que fallaron.
    /// Idempotente.
    /// </summary>
    [HttpPost("correspondence/admin/repair-cross-customer-threads")]
    [RateLimitExempt("Reposición de plataforma (PlatformAdmin o M2M de la PlatformTenant), una corrida por oficina.")]
    [ProducesResponseType<RepairCrossCustomerThreadsReport>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RepairCrossCustomerThreads(
        [FromQuery] Guid tenantId,
        [FromQuery] bool dryRun = true,
        CancellationToken ct = default
    )
    {
        if (!IsPlatformCaller())
            return Forbid();
        if (tenantId == Guid.Empty)
            return BadRequest(new { error = "tenantId is required." });

        var report = await bus.InvokeAsync<RepairCrossCustomerThreadsReport>(
            new RepairCrossCustomerThreadsCommand(tenantId, dryRun),
            ct
        );
        return Ok(report);
    }

    // Un token de servicio solo vale si es de la PlatformTenant: el de una oficina no puede tocar otra.
    private bool IsPlatformCaller() =>
        User.IsPlatformAdmin()
        || (
            User.GetActorType() == ActorType.Service
            && User.TryGetTenantId(out var callerTenantId)
            && callerTenantId == PlatformTenant.Id
        );
}
