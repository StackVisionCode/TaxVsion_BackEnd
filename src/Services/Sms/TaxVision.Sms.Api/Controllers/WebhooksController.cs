using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Sms.Application.Webhooks.Commands;
using Wolverine;

namespace TaxVision.Sms.Api.Controllers;

/// <summary>
/// Webhooks del proveedor (DLR/estado + inbound STOP/START/HELP). Anónimo a nivel JWT — la autenticación
/// es la FIRMA del proveedor (verificada dentro del handler con el secreto propio del servicio). El Gateway
/// solo enruta; SMS es dueño del endpoint, el secreto, la verificación, el parsing y la persistencia.
/// </summary>
[ApiController]
[Route("sms/webhooks")]
[AllowAnonymous]
public sealed class WebhooksController(IMessageBus bus) : ControllerBase
{
    [HttpPost("{provider}/status")]
    public async Task<IActionResult> Status(string provider, CancellationToken ct)
    {
        var rawBody = await ReadBodyAsync(ct);
        var signature = ReadSignature();
        var result = await bus.InvokeAsync<Result>(new ProcessDeliveryReceiptCommand(provider, rawBody, signature), ct);
        return result.IsSuccess ? Ok() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{provider}/inbound")]
    public async Task<IActionResult> Inbound(string provider, CancellationToken ct)
    {
        var rawBody = await ReadBodyAsync(ct);
        var signature = ReadSignature();
        var result = await bus.InvokeAsync<Result>(new ProcessInboundCommand(provider, rawBody, signature), ct);
        return result.IsSuccess ? Ok() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private async Task<string> ReadBodyAsync(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        return await reader.ReadToEndAsync(ct);
    }

    // Header de firma por convención; los proveedores que usen otro nombre lo mapean en su config/adapter.
    private string ReadSignature() =>
        Request.Headers.TryGetValue("X-Signature", out var v) ? v.ToString()
        : Request.Headers.TryGetValue("X-Sms-Signature", out var v2) ? v2.ToString()
        : string.Empty;
}
