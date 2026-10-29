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
using TaxVision.Sms.Application.Webhooks.Commands;
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

    // Cuerpos de los POST .../search: el término (nombre, teléfono o email del cliente) viaja aquí y no
    // en la URL, que queda en los logs del Gateway/proxy. Mismos filtros y defaults que el GET.
    public sealed record SearchMessagesRequest(
        Guid? CustomerId = null,
        SmsMessageStatusFilter Status = SmsMessageStatusFilter.All,
        string? Term = null,
        DateTime? From = null,
        DateTime? To = null,
        int Page = 1,
        int Size = 20
    );

    public sealed record SearchConversationsRequest(string? Term = null, int Page = 1, int Size = 20);

    public sealed record SearchOptOutsRequest(
        SmsOptOutStatusFilter Status = SmsOptOutStatusFilter.All,
        string? Term = null,
        int Page = 1,
        int Size = 20
    );

    // `term` en el GET solo por compatibilidad (bundles viejos): el CRM busca por POST messages/search.
    [HttpGet("messages")]
    [RateLimit("sms.h.read")]
    [ProducesResponseType<PagedResult<SmsMessageSummaryResponse>>(StatusCodes.Status200OK)]
    public Task<IActionResult> ListMessages(
        [FromQuery] Guid? customerId,
        [FromQuery] SmsMessageStatusFilter status = SmsMessageStatusFilter.All,
        [FromQuery] string? term = null,
        [FromQuery(Name = "from")] DateTime? fromUtc = null,
        [FromQuery(Name = "to")] DateTime? toUtc = null,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20,
        CancellationToken ct = default
    ) => RunMessagesSearchAsync(new SearchMessagesRequest(customerId, status, term, fromUtc, toUtc, page, size), ct);

    // `no-store`: el resultado lleva PII y no debe quedar en caches intermedios.
    [HttpPost("messages/search")]
    [RateLimit("sms.h.read")]
    [ProducesResponseType<PagedResult<SmsMessageSummaryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<IActionResult> SearchMessages([FromBody] SearchMessagesRequest body, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return RunMessagesSearchAsync(body, ct);
    }

    [HttpGet("conversations")]
    [RateLimit("sms.h.read")]
    [ProducesResponseType<PagedResult<SmsConversationSummaryResponse>>(StatusCodes.Status200OK)]
    public Task<IActionResult> ListConversations(
        [FromQuery] string? term = null,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20,
        CancellationToken ct = default
    ) => RunConversationsSearchAsync(new SearchConversationsRequest(term, page, size), ct);

    [HttpPost("conversations/search")]
    [RateLimit("sms.h.read")]
    [ProducesResponseType<PagedResult<SmsConversationSummaryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<IActionResult> SearchConversations([FromBody] SearchConversationsRequest body, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return RunConversationsSearchAsync(body, ct);
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

    /// <summary>Reconcilia AHORA el estado de los SMS atascados en Accepted de este tenant, consultando por
    /// pull a cada proveedor (backstop del DLR por webhook). Idempotente. Acotado al tenant del JWT.</summary>
    [HttpPost("messages/reconcile")]
    [RateLimit("sms.i.reconcile")]
    [ProducesResponseType<ReconcileSmsStatusesResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reconcile(
        [FromQuery] int max = 200,
        [FromQuery] int minAgeSeconds = 0,
        CancellationToken ct = default
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<ReconcileSmsStatusesResponse>>(
            new ReconcileSmsStatusesCommand(tenantId, max, minAgeSeconds),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("optouts")]
    [RateLimit("sms.h.read")]
    [ProducesResponseType<PagedResult<SmsOptOutSummaryResponse>>(StatusCodes.Status200OK)]
    public Task<IActionResult> ListOptOuts(
        [FromQuery] SmsOptOutStatusFilter status = SmsOptOutStatusFilter.All,
        [FromQuery] string? term = null,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20,
        CancellationToken ct = default
    ) => RunOptOutsSearchAsync(new SearchOptOutsRequest(status, term, page, size), ct);

    [HttpPost("optouts/search")]
    [RateLimit("sms.h.read")]
    [ProducesResponseType<PagedResult<SmsOptOutSummaryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<IActionResult> SearchOptOuts([FromBody] SearchOptOutsRequest body, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return RunOptOutsSearchAsync(body, ct);
    }

    private async Task<IActionResult> RunMessagesSearchAsync(SearchMessagesRequest search, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var canViewAll = await permissionsSource.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);
        var result = await bus.InvokeAsync<PagedResult<SmsMessageSummaryResponse>>(
            new SearchSmsMessagesQuery(
                tenantId,
                search.CustomerId,
                search.Status,
                search.Term,
                search.From,
                search.To,
                CrmSourceContext,
                search.Page,
                search.Size,
                userId,
                canViewAll
            ),
            ct
        );
        return Ok(result);
    }

    private async Task<IActionResult> RunConversationsSearchAsync(
        SearchConversationsRequest search,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var canViewAll = await permissionsSource.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);
        var result = await bus.InvokeAsync<PagedResult<SmsConversationSummaryResponse>>(
            new SearchSmsConversationsQuery(
                tenantId,
                search.Term,
                CrmSourceContext,
                search.Page,
                search.Size,
                userId,
                canViewAll
            ),
            ct
        );
        return Ok(result);
    }

    private async Task<IActionResult> RunOptOutsSearchAsync(SearchOptOutsRequest search, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var canViewAll = await permissionsSource.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);
        var result = await bus.InvokeAsync<PagedResult<SmsOptOutSummaryResponse>>(
            new SearchSmsOptOutsQuery(
                tenantId,
                search.Status,
                search.Term,
                search.Page,
                search.Size,
                userId,
                canViewAll
            ),
            ct
        );
        return Ok(result);
    }
}
