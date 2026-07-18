using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.PaymentClient.Api.Authorization;
using TaxVision.PaymentClient.Api.Common;
using TaxVision.PaymentClient.Application.TenantPaymentConfigs.Commands.CreateTenantPaymentConfig;
using TaxVision.PaymentClient.Application.TenantPaymentConfigs.Commands.DeactivateTenantPaymentConfig;
using TaxVision.PaymentClient.Application.TenantPaymentConfigs.Commands.UpdateTenantPaymentConfigSecrets;
using TaxVision.PaymentClient.Application.TenantPaymentConfigs.Queries;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentClient.Api.Controllers;

[ApiController]
[Route("payments-client/config")]
[Authorize]
public sealed class TenantPaymentConfigsController(IMessageBus bus) : ControllerBase
{
    [HttpGet("{provider}")]
    [HasPermission(PaymentClientPermissions.ConfigRead)]
    [ProducesResponseType<TenantPaymentConfigResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(PaymentProviderCode provider, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TenantPaymentConfigResponse>>(new GetTenantPaymentConfigQuery(tenantId, provider), ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record CreateConfigRequest(PaymentProviderCode ProviderCode, TenantPaymentMode Mode, string PublishableKey, string StatementDescriptor);

    [HttpPost]
    [HasPermission(PaymentClientPermissions.ConfigManage)]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(CreateConfigRequest request, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<Guid>>(
            new CreateTenantPaymentConfigCommand(tenantId, request.ProviderCode, request.Mode, request.PublishableKey, request.StatementDescriptor, userId), ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record UpdateSecretsRequest(string SecretKey, string WebhookSecret);

    /// <summary>El secreto llega en texto plano SOLO en este request HTTPS — el handler lo
    /// cifra con <c>ISecretProtector</c> antes de persistirlo, nunca se loguea.</summary>
    [HttpPut("{provider}/secrets")]
    [HasPermission(PaymentClientPermissions.ConfigManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateSecrets(PaymentProviderCode provider, UpdateSecretsRequest request, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new UpdateTenantPaymentConfigSecretsCommand(tenantId, provider, request.SecretKey, request.WebhookSecret, userId), ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record DeactivateConfigRequest(string Reason);

    [HttpPost("{provider}/deactivate")]
    [HasPermission(PaymentClientPermissions.ConfigManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Deactivate(PaymentProviderCode provider, DeactivateConfigRequest request, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new DeactivateTenantPaymentConfigCommand(tenantId, provider, request.Reason, userId), ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
