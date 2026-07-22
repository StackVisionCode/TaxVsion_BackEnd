using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Notification.Api.Common;
using TaxVision.Notification.Application.Email.Templates;
using TaxVision.Notification.Application.Email.Templates.Commands;
using TaxVision.Notification.Application.Email.Templates.Queries;
using TaxVision.Notification.Domain.Emailing;
using Wolverine;

namespace TaxVision.Notification.Api.Controllers;

/// <summary>
/// Gestión de plantillas de correo (System/Tenant). La BD guarda metadata + storage keys; el HTML,
/// design JSON y preview se guardan en CloudStorage. Cada edición crea una versión; publicar activa una.
///
/// NO retirado en la Fase 18 del plan de hardening (Notification), a pesar de que el frontend no
/// llama a este controller (confirmado por el usuario) y de que Scribe existe para reemplazarlo:
/// es el ÚNICO punto de entrada dentro de Notification para crear/versionar/publicar/archivar un
/// <see cref="TaxVision.Notification.Domain.Emailing.Templates.EmailTemplate"/> — no hay seeder ni
/// ningún otro caller que inserte filas en esa tabla. <c>EmailCampaigns</c> (fuera de alcance de este
/// plan por instrucción explícita del usuario, ver <c>EmailCampaignsController</c>/
/// <c>ScheduleEmailCampaignHandler</c>/<c>CreateEmailCampaignHandler</c>) exige un
/// <c>EmailTemplate</c> Activo con versión publicada para programar una campaña — retirar este
/// controller habría dejado a EmailCampaigns sin forma de crear una plantilla nueva jamás, aunque
/// su propio controller siguiera compilando. Solo se retiró el envío ad-hoc por plantilla
/// (<c>POST /notifications/email/send-template</c> en <c>EmailSendController</c>), que sí estaba
/// genuinamente muerto y sin ningún caller interno.
/// </summary>
[ApiController]
[Route("notifications/email/templates")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class EmailTemplatesController(IMessageBus bus) : ControllerBase
{
    public sealed record CreateEmailTemplateRequest(
        EmailScope Scope,
        string TemplateKey,
        string Subject,
        string? Description = null,
        string? Category = null,
        IReadOnlyList<string>? Variables = null
    );

    public sealed record AddVersionRequest(
        string SubjectTemplate,
        string Html,
        string? DesignJson = null,
        string? PreviewPngBase64 = null
    );

    public sealed record PublishRequest(Guid VersionId);

    [HttpPost]
    [HasPermission(NotificationPermissions.TemplateManage)]
    [ProducesResponseType<EmailTemplateResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateEmailTemplateRequest request, CancellationToken ct)
    {
        User.TryGetTenantId(out var tenantId);
        var command = new CreateEmailTemplateCommand(
            request.Scope,
            request.Scope == EmailScope.Tenant ? tenantId : null,
            User.IsPlatformAdmin(),
            User.TryGetUserId(out var userId) ? userId : null,
            request.TemplateKey,
            request.Subject,
            request.Description,
            request.Category,
            request.Variables
        );
        var result = await bus.InvokeAsync<Result<EmailTemplateResponse>>(command, ct);
        return result.IsSuccess
            ? Created($"/notifications/email/templates/{result.Value.Id}", result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet]
    [HasPermission(NotificationPermissions.TemplateView)]
    [ProducesResponseType<IReadOnlyList<EmailTemplateResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        Guid? tenantId = User.TryGetTenantId(out var t) ? t : null;
        var result = await bus.InvokeAsync<Result<IReadOnlyList<EmailTemplateResponse>>>(
            new GetEmailTemplatesQuery(tenantId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(NotificationPermissions.TemplateView)]
    [ProducesResponseType<EmailTemplateDetailResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        Guid? tenantId = User.TryGetTenantId(out var t) ? t : null;
        var result = await bus.InvokeAsync<Result<EmailTemplateDetailResponse>>(
            new GetEmailTemplateByIdQuery(id, tenantId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/versions")]
    [HasPermission(NotificationPermissions.TemplateManage)]
    [ProducesResponseType<EmailTemplateVersionResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> AddVersion(Guid id, [FromBody] AddVersionRequest request, CancellationToken ct)
    {
        Guid? tenantId = User.TryGetTenantId(out var t) ? t : null;
        var command = new AddEmailTemplateVersionCommand(
            id,
            tenantId,
            User.IsPlatformAdmin(),
            User.TryGetUserId(out var userId) ? userId : null,
            request.SubjectTemplate,
            request.Html,
            request.DesignJson,
            DecodeBase64(request.PreviewPngBase64)
        );
        var result = await bus.InvokeAsync<Result<EmailTemplateVersionResponse>>(command, ct);
        return result.IsSuccess
            ? Created($"/notifications/email/templates/{id}", result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/publish")]
    [HasPermission(NotificationPermissions.TemplateManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Publish(Guid id, [FromBody] PublishRequest request, CancellationToken ct)
    {
        Guid? tenantId = User.TryGetTenantId(out var t) ? t : null;
        var result = await bus.InvokeAsync<Result>(
            new PublishEmailTemplateCommand(id, request.VersionId, tenantId, User.IsPlatformAdmin()),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/archive")]
    [HasPermission(NotificationPermissions.TemplateManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        Guid? tenantId = User.TryGetTenantId(out var t) ? t : null;
        var result = await bus.InvokeAsync<Result>(
            new ArchiveEmailTemplateCommand(id, tenantId, User.IsPlatformAdmin()),
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
