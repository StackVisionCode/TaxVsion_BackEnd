using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Sms.Application.OptOut.Commands;
using TaxVision.Sms.Application.OptOut.Queries;
using Wolverine;

namespace TaxVision.Sms.Api.Controllers;

/// <summary>Gestión manual del consentimiento (bajas/altas) desde el CRM. Admin del tenant, exige
/// <c>sms.manage</c>. El STOP/START entrante del cliente sigue funcionando aparte por webhook.</summary>
[ApiController]
[Route("sms")]
[Authorize]
[AllowActorTypes(ActorType.TenantAdmin)]
[HasPermission(SmsPermissions.Manage)]
public sealed class SmsConsentController(IMessageBus bus, ITenantContext tenant) : ControllerBase
{
    public sealed record SetConsentRequest(Guid CustomerId, string Phone, SmsConsentAction Action);

    [HttpPost("optouts")]
    [RateLimit("sms.h.manage")]
    [ProducesResponseType<SmsOptOutSummaryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetConsent([FromBody] SetConsentRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<SmsOptOutSummaryResponse>>(
            new SetSmsConsentCommand(tenant.TenantId, request.CustomerId, request.Phone, request.Action),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
