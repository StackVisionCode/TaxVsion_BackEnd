using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Sms.Application.Messages.Queries;
using TaxVision.Sms.Application.OptOut.Queries;
using Wolverine;

namespace TaxVision.Sms.Api.Controllers;

/// <summary>Read model del CRM: historial de SMS, detalle, stats y bajas (opt-outs). Solo lectura, el
/// tenant sale del JWT. Exige <c>sms.read</c>.</summary>
[ApiController]
[Route("sms")]
[Authorize]
[AllowActorTypes(ActorType.Service, ActorType.TenantAdmin, ActorType.TenantEmployee)]
[HasPermission(SmsPermissions.Read)]
public sealed class SmsReadController(IMessageBus bus, IUserPermissionsSource permissionsSource) : ControllerBase
{
    private const int DefaultStatsWindowDays = 30;

    // Alcance del módulo del CRM: solo los SMS que el preparador envió DESDE aquí (el compose marca
    // sourceContext="crm-sms"). Excluye los mensajes de sistema (OTP de firma, notificaciones) que
    // otros módulos enrutan por el servicio SMS — no son textos del preparador y un OTP no debe verse.
    private const string CrmSourceContext = "crm-sms";

    [HttpGet("messages")]
    [RateLimit("sms.h.read")]
    [ProducesResponseType<PagedResult<SmsMessageSummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMessages(
        [FromQuery] Guid? customerId,
        [FromQuery] SmsMessageStatusFilter status = SmsMessageStatusFilter.All,
        [FromQuery] string? term = null,
        [FromQuery(Name = "from")] DateTime? fromUtc = null,
        [FromQuery(Name = "to")] DateTime? toUtc = null,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20,
        CancellationToken ct = default
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var canViewAll = await permissionsSource.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);
        var result = await bus.InvokeAsync<PagedResult<SmsMessageSummaryResponse>>(
            new SearchSmsMessagesQuery(
                tenantId,
                customerId,
                status,
                term,
                fromUtc,
                toUtc,
                CrmSourceContext,
                page,
                size,
                userId,
                canViewAll
            ),
            ct
        );
        return Ok(result);
    }

    [HttpGet("messages/stats")]
    [RateLimit("sms.h.read")]
    [ProducesResponseType<SmsStatsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Stats(
        [FromQuery(Name = "from")] DateTime? fromUtc = null,
        [FromQuery(Name = "to")] DateTime? toUtc = null,
        CancellationToken ct = default
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var to = toUtc ?? DateTime.UtcNow;
        var from = fromUtc ?? to.AddDays(-DefaultStatsWindowDays);
        var canViewAll = await permissionsSource.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);
        var result = await bus.InvokeAsync<SmsStatsResponse>(
            new GetSmsStatsQuery(tenantId, from, to, CrmSourceContext, userId, canViewAll),
            ct
        );
        return Ok(result);
    }

    [HttpGet("messages/{id:guid}")]
    [RateLimit("sms.h.read")]
    [ProducesResponseType<SmsMessageDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMessage(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var canViewAll = await permissionsSource.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);
        var result = await bus.InvokeAsync<Result<SmsMessageDetailResponse>>(
            new GetSmsMessageByIdQuery(tenantId, id, userId, canViewAll),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("optouts")]
    [RateLimit("sms.h.read")]
    [ProducesResponseType<PagedResult<SmsOptOutSummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListOptOuts(
        [FromQuery] SmsOptOutStatusFilter status = SmsOptOutStatusFilter.All,
        [FromQuery] string? term = null,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20,
        CancellationToken ct = default
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var canViewAll = await permissionsSource.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);
        var result = await bus.InvokeAsync<PagedResult<SmsOptOutSummaryResponse>>(
            new SearchSmsOptOutsQuery(tenantId, status, term, page, size, userId, canViewAll),
            ct
        );
        return Ok(result);
    }
}
