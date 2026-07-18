using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Notification.Api.Authorization;
using TaxVision.Notification.Api.Common;
using TaxVision.Notification.Application.Email.Layouts;
using TaxVision.Notification.Application.Email.Layouts.Commands;
using TaxVision.Notification.Application.Email.Layouts.Queries;
using TaxVision.Notification.Domain.Emailing;
using Wolverine;

namespace TaxVision.Notification.Api.Controllers;

/// <summary>
/// Gestión de layouts de correo (System/Tenant). El layout envuelve el cuerpo de la plantilla
/// (marcador {{ body }}). Un layout default por scope/tenant; fallback al global del SaaS.
///
/// NO retirado en la Fase 18 del plan de hardening (Notification) por el mismo motivo exacto que
/// <see cref="EmailTemplatesController"/>: es el único punto de entrada para crear/marcar-default un
/// <see cref="TaxVision.Notification.Domain.Emailing.Layouts.EmailLayout"/>, y
/// <c>ScheduleEmailCampaignHandler</c> (EmailCampaigns, fuera de alcance de este plan) lee el layout
/// default vía <c>IEmailLayoutRepository.GetDefaultAsync</c> al programar una campaña.
/// </summary>
[ApiController]
[Route("notifications/email/layouts")]
[Authorize]
public sealed class EmailLayoutsController(IMessageBus bus) : ControllerBase
{
    public sealed record CreateEmailLayoutRequest(
        EmailScope Scope,
        string LayoutName,
        string Html,
        string? DesignJson = null,
        string? PreviewPngBase64 = null,
        bool IsDefault = false
    );

    [HttpPost]
    [HasPermission(NotificationPermissions.LayoutManage)]
    [ProducesResponseType<EmailLayoutResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateEmailLayoutRequest request, CancellationToken ct)
    {
        User.TryGetTenantId(out var tenantId);
        var command = new CreateEmailLayoutCommand(
            request.Scope,
            request.Scope == EmailScope.Tenant ? tenantId : null,
            User.IsPlatformAdmin(),
            User.TryGetUserId(out var userId) ? userId : null,
            request.LayoutName,
            request.Html,
            request.DesignJson,
            DecodeBase64(request.PreviewPngBase64),
            request.IsDefault
        );
        var result = await bus.InvokeAsync<Result<EmailLayoutResponse>>(command, ct);
        return result.IsSuccess
            ? Created($"/notifications/email/layouts/{result.Value.Id}", result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet]
    [HasPermission(NotificationPermissions.TemplateView)]
    [ProducesResponseType<IReadOnlyList<EmailLayoutResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        Guid? tenantId = User.TryGetTenantId(out var t) ? t : null;
        var result = await bus.InvokeAsync<Result<IReadOnlyList<EmailLayoutResponse>>>(
            new GetEmailLayoutsQuery(tenantId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/set-default")]
    [HasPermission(NotificationPermissions.LayoutManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetDefault(Guid id, CancellationToken ct)
    {
        Guid? tenantId = User.TryGetTenantId(out var t) ? t : null;
        var result = await bus.InvokeAsync<Result>(
            new SetDefaultEmailLayoutCommand(id, tenantId, User.IsPlatformAdmin()),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private static byte[]? DecodeBase64(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
