using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Billing.Api.Authorization;
using TaxVision.Billing.Application.Settings;
using Wolverine;

namespace TaxVision.Billing.Api.Controllers;

/// <summary>Ajustes de facturación del tenant: plazo de pago, instrucciones de pago y pie de la factura.
/// Cada factura los congela al crear el borrador.</summary>
[ApiController]
[Route("billing/invoice-settings")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class InvoiceSettingsController(IMessageBus bus) : ControllerBase
{
    // Mismas cuotas que el perfil del emisor (lectura F / gestión G); el catálogo vive en BuildingBlocks.
    [HttpGet]
    [RateLimit("billing.f.issuer_profile_read")]
    [HasPermission(InvoicingPermissions.View)]
    [ProducesResponseType<InvoiceSettingsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<InvoiceSettingsResponse>>(new GetInvoiceSettingsQuery(tenantId), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record UpdateInvoiceSettingsRequest(
        string? PaymentTerms,
        string? PaymentInstructions,
        string? InvoiceFooter
    );

    // Cómo cobra la oficina lo decide el dueño, igual que el emisor legal: misma permission.
    [HttpPut]
    [RateLimit("billing.g.issuer_profile_manage")]
    [HasPermission(InvoicingPermissions.IssuerManage)]
    [ProducesResponseType<InvoiceSettingsResponse>(StatusCodes.Status200OK)]
    // Plazo fuera de la lista o texto de más de 500 caracteres → 400 con el código del campo.
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(UpdateInvoiceSettingsRequest request, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<InvoiceSettingsResponse>>(
            new UpdateInvoiceSettingsCommand(
                tenantId,
                request.PaymentTerms,
                request.PaymentInstructions,
                request.InvoiceFooter
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
