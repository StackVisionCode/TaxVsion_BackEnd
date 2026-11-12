using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.Catalog.Migrations;
using Wolverine;

namespace TaxVision.Subscription.Api.Controllers.Admin;

/// <summary>
/// D11 — mover a las oficinas de un plan a otra edición. "Immediate" solo si ninguna oficina pierde nada;
/// "AtNextRenewal" les avisa y aplica en la primera renovación tras el aviso. Iniciar exige el token de la vista previa
/// y un motivo; el trabajo lo hace un job, una oficina por transacción.
/// </summary>
[ApiController]
[Route("admin/subscription/plans/{planId:guid}/migrations")]
[Authorize]
[HasPermission(SubscriptionPermissions.CatalogManage)]
[AllowActorTypes(ActorType.PlatformAdmin)]
public sealed class SubscriberMigrationsAdminController(IMessageBus bus) : ControllerBase
{
    /// <summary>Qué cambia, si es pura mejora y qué oficinas entrarían o quedarían bloqueadas (y por qué).</summary>
    [HttpPost("preview")]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType<SubscriberMigrationPreview>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Preview(
        Guid planId,
        [FromBody] SubscriberMigrationPreviewRequest request,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<Result<SubscriberMigrationPreview>>(
            new PreviewSubscriberMigrationQuery(planId, request.ToVersionId, request.Mode, request.NoticeDays),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : MigrationErrors.ToResponse(this, result.Error);
    }

    [HttpPost]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> Start(
        Guid planId,
        [FromBody] StartSubscriberMigrationRequest request,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out _, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<Guid>>(
            new StartSubscriberMigrationCommand(
                planId,
                request.ToVersionId,
                request.Mode,
                request.NoticeDays,
                request.Reason,
                request.PreviewToken,
                userId
            ),
            ct
        );
        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { planId, migrationId = result.Value }, new { id = result.Value })
            : MigrationErrors.ToResponse(this, result.Error);
    }

    /// <summary>Estado de la migración y el resultado por oficina.</summary>
    [HttpGet("{migrationId:guid}")]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType<SubscriberMigrationResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid planId, Guid migrationId, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<SubscriberMigrationResponse>>(
            new GetSubscriberMigrationQuery(migrationId),
            ct
        );
        if (result.IsSuccess && result.Value.PlanId != planId)
            return NotFound();
        return result.IsSuccess ? Ok(result.Value) : MigrationErrors.ToResponse(this, result.Error);
    }

    /// <summary>Solo las agendadas: las oficinas que todavía no cambiaron vuelven a su edición. Exige motivo.</summary>
    [HttpPost("{migrationId:guid}/cancel")]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(
        Guid planId,
        Guid migrationId,
        [FromBody] CancelSubscriberMigrationRequest request,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out _, out var userId))
            return Unauthorized();

        var current = await bus.InvokeAsync<Result<SubscriberMigrationResponse>>(
            new GetSubscriberMigrationQuery(migrationId),
            ct
        );
        if (current.IsSuccess && current.Value.PlanId != planId)
            return NotFound();

        var result = await bus.InvokeAsync<Result>(
            new CancelSubscriberMigrationCommand(migrationId, request.Reason, userId),
            ct
        );
        return result.IsSuccess ? NoContent() : MigrationErrors.ToResponse(this, result.Error);
    }
}

public sealed record SubscriberMigrationPreviewRequest(Guid ToVersionId, string Mode, int NoticeDays = 30);

public sealed record StartSubscriberMigrationRequest(
    Guid ToVersionId,
    string Mode,
    int NoticeDays,
    string? Reason,
    string? PreviewToken
);

public sealed record CancelSubscriberMigrationRequest(string? Reason);

/// <summary>Errores propios de las migraciones; el resto cae en <see cref="CatalogErrors"/>.</summary>
public static class MigrationErrors
{
    public static IActionResult ToResponse(ControllerBase controller, Error error) =>
        error.Code switch
        {
            "Migration.InvalidMode" or "Migration.NoticeTooShort" => controller.BadRequest(error),
            "Migration.ImmediateRequiresImprovement" or "Migration.NoOffices" or "Migration.CannotCancel" =>
                controller.Conflict(error),
            "Migration.NotFound" or "Migration.VersionNotFound" => controller.NotFound(error),
            _ => CatalogErrors.ToResponse(controller, error),
        };
}
