using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using TaxVision.Tenant.Api.Common;
using TaxVision.Tenant.Api.Contracts;
using TaxVision.Tenant.Application.Team;
using TaxVision.Tenant.Application.Team.Commands;
using TaxVision.Tenant.Application.Team.Queries;
using TaxVision.Tenant.Domain.Team;
using Wolverine;

namespace TaxVision.Tenant.Api.Controllers;

/// <summary>
/// Preferencias del espacio de trabajo (layout del dashboard del CRM). Las rutas <c>me</c> son del que llama; la
/// plantilla de la oficina exige <c>settings.manage</c>. Tenant y usuario, siempre del JWT. El parámetro se llama
/// <c>key</c>: <c>token</c> activaría otra partición del limitador.
/// </summary>
[ApiController]
[Route("team")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin)]
public sealed class WorkspacePreferencesController(IMessageBus bus) : ControllerBase
{
    private const int MaxBodyBytes = 16_384;

    [HttpGet("me/workspace-preferences/{key}")]
    [RateLimit("tenant.f.preferences_read")]
    [ProducesResponseType<WorkspacePreferenceView>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetMine(string key, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<WorkspacePreferenceView>>(
            new GetMyWorkspacePreferenceQuery(tenantId, userId, key),
            ct
        );
        return Respond(result, view => view);
    }

    [HttpPut("me/workspace-preferences/{key}")]
    [RateLimit("tenant.g.preferences_write")]
    [RequestSizeLimit(MaxBodyBytes)]
    [ProducesResponseType<WorkspacePreferenceView>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<Error>(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public Task<IActionResult> SaveMine(
        string key,
        [FromBody] SaveWorkspacePreferenceRequest request,
        CancellationToken ct
    ) => SaveAsync(key, request, forOffice: false, ct);

    [HttpDelete("me/workspace-preferences/{key}")]
    [RateLimit("tenant.g.preferences_write")]
    [ProducesResponseType<WorkspacePreferenceView>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<Error>(StatusCodes.Status409Conflict)]
    public Task<IActionResult> ClearMine(
        string key,
        [FromQuery, BindRequired] long expectedVersion,
        CancellationToken ct
    ) => ClearAsync(key, expectedVersion, forOffice: false, ct);

    [HttpPut("workspace-preferences/{key}/office-default")]
    [HasPermission(OfficeSettingsPermissions.Manage)]
    [RateLimit("tenant.g.preferences_write")]
    [RequestSizeLimit(MaxBodyBytes)]
    [ProducesResponseType<OfficeDefaultView>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<Error>(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public Task<IActionResult> SaveOfficeDefault(
        string key,
        [FromBody] SaveWorkspacePreferenceRequest request,
        CancellationToken ct
    ) => SaveAsync(key, request, forOffice: true, ct);

    [HttpDelete("workspace-preferences/{key}/office-default")]
    [HasPermission(OfficeSettingsPermissions.Manage)]
    [RateLimit("tenant.g.preferences_write")]
    [ProducesResponseType<OfficeDefaultView>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<Error>(StatusCodes.Status409Conflict)]
    public Task<IActionResult> ClearOfficeDefault(
        string key,
        [FromQuery, BindRequired] long expectedVersion,
        CancellationToken ct
    ) => ClearAsync(key, expectedVersion, forOffice: true, ct);

    public static int StatusOf(Error error) =>
        error == WorkspacePreferenceErrors.VersionConflict
            ? StatusCodes.Status409Conflict
            : StatusCodes.Status400BadRequest;

    private async Task<IActionResult> SaveAsync(
        string key,
        SaveWorkspacePreferenceRequest request,
        bool forOffice,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var command = new SaveWorkspacePreferenceCommand(
            tenantId,
            forOffice ? null : userId,
            userId,
            key,
            request.SchemaVersion,
            request.ExpectedVersion,
            request.Payload.GetRawText()
        );
        return Respond(await bus.InvokeAsync<Result<WorkspacePreferenceView>>(command, ct), Shape(forOffice));
    }

    private async Task<IActionResult> ClearAsync(string key, long expectedVersion, bool forOffice, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var command = new ClearWorkspacePreferenceCommand(
            tenantId,
            forOffice ? null : userId,
            userId,
            key,
            expectedVersion
        );
        return Respond(await bus.InvokeAsync<Result<WorkspacePreferenceView>>(command, ct), Shape(forOffice));
    }

    // Las rutas de la plantilla devuelven solo su parte. Quitar una plantilla que nunca existió no tiene vista.
    private static Func<WorkspacePreferenceView, object?> Shape(bool forOffice) =>
        forOffice ? view => view.OfficeDefault : view => view;

    private IActionResult Respond(Result<WorkspacePreferenceView> result, Func<WorkspacePreferenceView, object?> shape)
    {
        if (result.IsFailure)
            return StatusCode(StatusOf(result.Error), result.Error);
        return shape(result.Value) is { } body ? Ok(body) : Ok();
    }
}
