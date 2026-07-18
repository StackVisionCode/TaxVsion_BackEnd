using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.PaymentClient.Api.Authorization;
using TaxVision.PaymentClient.Api.Common;
using TaxVision.PaymentClient.Application.TenantConnect.Commands.InitiateStripeConnectOnboarding;
using TaxVision.PaymentClient.Application.TenantConnect.Queries;
using TaxVision.PaymentClient.Domain.Connect;
using Wolverine;

namespace TaxVision.PaymentClient.Api.Controllers;

[ApiController]
[Route("payments-client/connect")]
[Authorize]
public sealed class TenantConnectAccountController(IMessageBus bus) : ControllerBase
{
    public sealed record OnboardRequest(ConnectAccountType Type, string Email, string RefreshUrl, string ReturnUrl);

    /// <summary>Idempotente: reintentar con un formulario a medio llenar reusa la misma
    /// Connected Account y solo emite un <c>AccountLink</c> nuevo (los links de Stripe
    /// expiran a los pocos minutos).</summary>
    [HttpPost("onboard")]
    [HasPermission(PaymentClientPermissions.ConnectAccountOnboard)]
    [ProducesResponseType<InitiateStripeConnectOnboardingResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Onboard(OnboardRequest request, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<InitiateStripeConnectOnboardingResponse>>(
            new InitiateStripeConnectOnboardingCommand(
                tenantId,
                request.Type,
                request.Email,
                request.RefreshUrl,
                request.ReturnUrl,
                userId
            ),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("status")]
    [HasPermission(PaymentClientPermissions.ConnectAccountRead)]
    [ProducesResponseType<TenantConnectAccountResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TenantConnectAccountResponse>>(
            new GetTenantConnectAccountQuery(tenantId),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
