using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Api.Authorization;
using TaxVision.Auth.Api.Common;
using TaxVision.Auth.Application.Audit.Queries;
using TaxVision.Auth.Domain.Roles;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>
/// Endpoints de consulta del registro de auditoría de autenticación del tenant.
/// Requiere el permiso de visualización de auditoría.
/// </summary>
[ApiController]
[Route("auth/audit")]
[HasPermission(PermissionCatalog.AuditView)]
public sealed class AuditController(IMessageBus bus) : ControllerBase
{
    /// <summary>Devuelve los eventos de auditoría paginados, con filtros opcionales por usuario, acción y rango de fechas.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<AuditLogResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAuditLogs(
        [FromQuery] Guid? userId = null,
        [FromQuery] string? action = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        [FromQuery] int page = 1,
        [FromQuery] int size = 50,
        CancellationToken ct = default)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<PagedResult<AuditLogResponse>>>(
            new GetAuditLogsQuery(tenantId, userId, action, fromUtc, toUtc, page, size),
            ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
