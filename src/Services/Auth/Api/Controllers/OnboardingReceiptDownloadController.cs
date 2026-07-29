using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaxVision.Auth.Application.Onboarding.ReceiptDownload.Queries;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>PayFlow (Fase 11) — mediador anónimo entre el link del email de recibo (Fase 12) y la
/// URL presignada real de CloudStorage (que expira en minutos). En cada click pide una fresca y
/// hace 302 redirect — así el link embebido en el email nunca vence. Ver
/// GetOnboardingReceiptDownloadRedirectQuery para el razonamiento de por qué el FileId funciona
/// como capability opaca sin autenticación adicional.</summary>
[ApiController]
[Route("onboarding/receipts")]
public sealed class OnboardingReceiptDownloadController(IMessageBus bus) : ControllerBase
{
    [HttpGet("{fileId:guid}/download")]
    [AllowAnonymous]
    [EnableRateLimiting("onboarding-receipt-download")]
    public async Task<IActionResult> Download(Guid fileId, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<Uri>>(new GetOnboardingReceiptDownloadRedirectQuery(fileId), ct);

        return result.IsSuccess
            ? Redirect(result.Value.ToString())
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
