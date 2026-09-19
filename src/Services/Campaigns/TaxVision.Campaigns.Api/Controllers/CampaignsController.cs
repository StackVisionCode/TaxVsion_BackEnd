using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Campaigns.Api.Requests;
using TaxVision.Campaigns.Application.Campaigns;
using TaxVision.Campaigns.Application.Campaigns.Commands;
using TaxVision.Campaigns.Application.Campaigns.Queries;
using TaxVision.Campaigns.Application.Runs;
using TaxVision.Campaigns.Application.Runs.Audience;
using TaxVision.Campaigns.Application.Runs.Commands;
using TaxVision.Campaigns.Application.Runs.Queries;
using TaxVision.Campaigns.Application.Scheduling;
using TaxVision.Campaigns.Application.Scheduling.Commands;
using TaxVision.Campaigns.Application.Scheduling.Queries;
using TaxVision.Campaigns.Domain.Campaigns;
using Wolverine;

namespace TaxVision.Campaigns.Api.Controllers;

/// <summary>
/// Orquestador de campañas — staff únicamente (TenantEmployee/TenantAdmin/PlatformAdmin). TenantId/
/// UserId SIEMPRE del JWT (<c>this.TryGetTenantAndUser</c>), nunca del body. Permiso
/// <c>campaigns.manage</c> (ya cableado en Auth). Slice 1: crear/listar/ver Draft.
/// </summary>
[ApiController]
[Route("campaigns")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class CampaignsController(IMessageBus bus) : ControllerBase
{
    private const int DefaultSize = 20;

    [HttpPost]
    [HasPermission(CampaignsPermissions.Manage)]
    [RateLimit("campaigns.g.create")]
    [ProducesResponseType<CampaignResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateCampaignRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<CampaignResponse>>(
            new CreateCampaignCommand(
                tenantId,
                userId,
                request.Name,
                request.ToChannelsFlag(),
                request.Message,
                request.Subject
            ),
            ct
        );
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet]
    [HasPermission(CampaignsPermissions.Manage)]
    [RateLimit("campaigns.f.list")]
    [ProducesResponseType<PagedResult<CampaignResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] CampaignStatus? status,
        [FromQuery] int page,
        [FromQuery] int size,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<PagedResult<CampaignResponse>>(
            new ListCampaignsQuery(tenantId, status, NormalizePage(page), NormalizeSize(size)),
            ct
        );
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(CampaignsPermissions.Manage)]
    [RateLimit("campaigns.f.get")]
    [ProducesResponseType<CampaignResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<CampaignResponse>>(new GetCampaignQuery(tenantId, id), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Selecciona el remitente (SenderProfile) de la campaña para un canal (solo en Draft).</summary>
    [HttpPost("{id:guid}/senders")]
    [HasPermission(CampaignsPermissions.Manage)]
    [RateLimit("campaigns.g.create")]
    [ProducesResponseType<CampaignResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetSender(Guid id, SetCampaignSenderRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<CampaignResponse>>(
            new SetCampaignSenderCommand(tenantId, id, request.Channel, request.SenderProfileId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/send-now")]
    [HasPermission(CampaignsPermissions.Manage)]
    [RateLimit("campaigns.g.send")]
    [ProducesResponseType<CampaignRunResponse>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> SendNow(Guid id, SendNowRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var recipients = (request.Recipients ?? [])
            .Select(r => new StartRunRecipient(r.ContactRef, r.Email, r.PhoneE164))
            .ToList();

        var result = await bus.InvokeAsync<Result<CampaignRunResponse>>(
            new StartCampaignRunCommand(tenantId, id, userId, recipients),
            ct
        );
        return result.IsSuccess
            ? AcceptedAtAction(nameof(GetRun), new { runId = result.Value.Id }, result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Envío inmediato resolviendo la audiencia desde listas de contactos y/o entradas manuales (opt-out/dedupe aplicados).</summary>
    [HttpPost("{id:guid}/send-to-audience")]
    [HasPermission(CampaignsPermissions.Manage)]
    [RateLimit("campaigns.g.send")]
    [ProducesResponseType<CampaignRunResponse>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> SendToAudience(Guid id, SendToAudienceRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var manual = (request.Manual ?? [])
            .Select(m => new ManualAudienceEntry(m.Email, m.PhoneE164))
            .ToList();

        var result = await bus.InvokeAsync<Result<CampaignRunResponse>>(
            new StartCampaignRunFromAudienceCommand(
                tenantId,
                id,
                userId,
                request.ContactListIds ?? [],
                manual,
                IncludeCustomers: request.IncludeCustomers
            ),
            ct
        );
        return result.IsSuccess
            ? AcceptedAtAction(nameof(GetRun), new { runId = result.Value.Id }, result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Agenda la campaña (una vez o recurrente). La audiencia se resuelve en cada disparo desde las listas.</summary>
    [HttpPost("{id:guid}/schedule")]
    [HasPermission(CampaignsPermissions.Manage)]
    [RateLimit("campaigns.g.send")]
    [ProducesResponseType<CampaignScheduleResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Schedule(Guid id, ScheduleCampaignRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<CampaignScheduleResponse>>(
            new ScheduleCampaignCommand(
                tenantId,
                id,
                request.Recurring,
                request.RunAtUtc,
                request.IntervalMinutes,
                request.ContactListIds ?? [],
                request.IncludeCustomers
            ),
            ct
        );
        return result.IsSuccess
            ? CreatedAtAction(nameof(ListSchedules), new { id }, result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{id:guid}/schedules")]
    [HasPermission(CampaignsPermissions.Manage)]
    [RateLimit("campaigns.f.list")]
    [ProducesResponseType<PagedResult<CampaignScheduleResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListSchedules(Guid id, [FromQuery] int page, [FromQuery] int size, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<PagedResult<CampaignScheduleResponse>>(
            new ListCampaignSchedulesQuery(tenantId, id, NormalizePage(page), NormalizeSize(size)),
            ct
        );
        return Ok(result);
    }

    /// <summary>Pausa / reanuda / cancela un agendado. <paramref name="action"/> ∈ {pause, resume, cancel}.</summary>
    [HttpPost("schedules/{scheduleId:guid}/{action}")]
    [HasPermission(CampaignsPermissions.Manage)]
    [RateLimit("campaigns.g.create")]
    [ProducesResponseType<CampaignScheduleResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetScheduleState(Guid scheduleId, string action, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        if (!Enum.TryParse<ScheduleAction>(action, ignoreCase: true, out var parsed))
            return BadRequest(new { code = "Schedule.UnknownAction", message = "Action must be pause, resume or cancel." });

        var result = await bus.InvokeAsync<Result<CampaignScheduleResponse>>(
            new SetScheduleStateCommand(tenantId, scheduleId, parsed),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{id:guid}/runs")]
    [HasPermission(CampaignsPermissions.Manage)]
    [RateLimit("campaigns.f.list")]
    [ProducesResponseType<PagedResult<CampaignRunResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListRuns(
        Guid id,
        [FromQuery] int page,
        [FromQuery] int size,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<PagedResult<CampaignRunResponse>>(
            new ListCampaignRunsQuery(tenantId, id, NormalizePage(page), NormalizeSize(size)),
            ct
        );
        return Ok(result);
    }

    [HttpGet("runs/{runId:guid}")]
    [HasPermission(CampaignsPermissions.Manage)]
    [RateLimit("campaigns.f.get")]
    [ProducesResponseType<CampaignRunResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRun(Guid runId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<CampaignRunResponse>>(new GetCampaignRunQuery(tenantId, runId), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private static int NormalizePage(int page) => page < 1 ? 1 : page;

    private static int NormalizeSize(int size) => size is < 1 or > 100 ? DefaultSize : size;
}
