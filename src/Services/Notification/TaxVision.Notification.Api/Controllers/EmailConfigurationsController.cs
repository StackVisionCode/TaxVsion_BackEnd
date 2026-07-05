using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Notification.Api.Authorization;
using TaxVision.Notification.Api.Common;
using TaxVision.Notification.Application.Email.Configurations;
using TaxVision.Notification.Application.Email.Configurations.Commands;
using TaxVision.Notification.Application.Email.Configurations.Queries;
using TaxVision.Notification.Domain.Emailing.Configurations;
using Wolverine;

namespace TaxVision.Notification.Api.Controllers;

/// <summary>
/// Gestión de configuraciones de proveedor de correo (SMTP/API), globales (System) o por tenant.
/// El <c>tenant_id</c> se toma del JWT; el scope System solo lo gestiona PlatformAdmin.
/// Los secretos nunca se devuelven (solo flags Has*).
/// </summary>
[ApiController]
[Route("notifications/email/configurations")]
[Authorize]
public sealed class EmailConfigurationsController(IMessageBus bus) : ControllerBase
{
    public sealed record CreateEmailConfigurationRequest(
        ProviderScope Scope,
        EmailProviderType ProviderType,
        string DisplayName,
        string FromEmail,
        string? FromName = null,
        string? Host = null,
        int? Port = null,
        string? Username = null,
        string? Password = null,
        bool UseSsl = true,
        string? ApiKey = null,
        string? ClientId = null,
        string? ClientSecret = null,
        string? TenantProviderId = null,
        bool IsDefault = false
    );

    public sealed record UpdateEmailConfigurationRequest(
        string DisplayName,
        string FromEmail,
        string? FromName = null,
        string? Host = null,
        int? Port = null,
        string? Username = null,
        string? Password = null,
        bool UseSsl = true,
        string? ApiKey = null,
        string? ClientId = null,
        string? ClientSecret = null,
        string? TenantProviderId = null
    );

    public sealed record TestEmailConfigurationRequest(string ToEmail);

    [HttpPost]
    [HasPermission(NotificationPermissions.SettingsManage)]
    [ProducesResponseType<EmailConfigurationResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateEmailConfigurationRequest request, CancellationToken ct)
    {
        User.TryGetTenantId(out var tenantId);
        if (request.Scope == ProviderScope.Tenant && tenantId == Guid.Empty)
            return Unauthorized();

        var command = new CreateEmailConfigurationCommand(
            request.Scope,
            request.Scope == ProviderScope.Tenant ? tenantId : null,
            User.IsPlatformAdmin(),
            request.ProviderType,
            request.DisplayName,
            request.FromEmail,
            request.FromName,
            request.Host,
            request.Port,
            request.Username,
            request.Password,
            request.UseSsl,
            request.ApiKey,
            request.ClientId,
            request.ClientSecret,
            request.TenantProviderId,
            request.IsDefault
        );

        var result = await bus.InvokeAsync<Result<EmailConfigurationResponse>>(command, ct);
        return result.IsSuccess
            ? Created($"/notifications/email/configurations/{result.Value.Id}", result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet]
    [HasPermission(NotificationPermissions.SettingsManage)]
    [ProducesResponseType<IReadOnlyList<EmailConfigurationResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        Guid? tenantId = User.TryGetTenantId(out var t) ? t : null;
        var result = await bus.InvokeAsync<Result<IReadOnlyList<EmailConfigurationResponse>>>(
            new GetEmailConfigurationsQuery(tenantId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(NotificationPermissions.SettingsManage)]
    [ProducesResponseType<EmailConfigurationResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        Guid? tenantId = User.TryGetTenantId(out var t) ? t : null;
        var result = await bus.InvokeAsync<Result<EmailConfigurationResponse>>(
            new GetEmailConfigurationByIdQuery(id, tenantId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(NotificationPermissions.SettingsManage)]
    [ProducesResponseType<EmailConfigurationResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateEmailConfigurationRequest request, CancellationToken ct)
    {
        Guid? tenantId = User.TryGetTenantId(out var t) ? t : null;
        var command = new UpdateEmailConfigurationCommand(
            id,
            tenantId,
            User.IsPlatformAdmin(),
            request.DisplayName,
            request.FromEmail,
            request.FromName,
            request.Host,
            request.Port,
            request.Username,
            request.Password,
            request.UseSsl,
            request.ApiKey,
            request.ClientId,
            request.ClientSecret,
            request.TenantProviderId
        );

        var result = await bus.InvokeAsync<Result<EmailConfigurationResponse>>(command, ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/set-default")]
    [HasPermission(NotificationPermissions.SettingsManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetDefault(Guid id, CancellationToken ct)
    {
        Guid? tenantId = User.TryGetTenantId(out var t) ? t : null;
        var result = await bus.InvokeAsync<Result>(
            new SetDefaultEmailConfigurationCommand(id, tenantId, User.IsPlatformAdmin()),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/test")]
    [HasPermission(NotificationPermissions.SettingsManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Test(Guid id, [FromBody] TestEmailConfigurationRequest request, CancellationToken ct)
    {
        Guid? tenantId = User.TryGetTenantId(out var t) ? t : null;
        var result = await bus.InvokeAsync<Result>(
            new TestEmailConfigurationCommand(id, tenantId, User.IsPlatformAdmin(), request.ToEmail),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
