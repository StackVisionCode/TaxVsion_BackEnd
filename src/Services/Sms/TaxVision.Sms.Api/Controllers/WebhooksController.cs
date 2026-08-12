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
        var result = await bus.InvokeAsync<Result>(
            new ProcessDeliveryReceiptCommand(provider, rawBody, ReadSignature(), BuildPublicUrl()),
            ct
        );
        return result.IsSuccess ? Ok() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{provider}/inbound")]
    public async Task<IActionResult> Inbound(string provider, CancellationToken ct)
    {
        var rawBody = await ReadBodyAsync(ct);
        var result = await bus.InvokeAsync<Result>(
            new ProcessInboundCommand(provider, rawBody, ReadSignature(), BuildPublicUrl()),
            ct
        );
        return result.IsSuccess ? Ok() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private async Task<string> ReadBodyAsync(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        return await reader.ReadToEndAsync(ct);
    }

    // Header de firma por convención; los proveedores que usen otro nombre lo mapean en su config/adapter.
    // Se incluye X-Twilio-Signature porque Twilio firma con su propio header.
    private string ReadSignature() =>
        Request.Headers.TryGetValue("X-Twilio-Signature", out var t) ? t.ToString()
        : Request.Headers.TryGetValue("X-Signature", out var v) ? v.ToString()
        : Request.Headers.TryGetValue("X-Sms-Signature", out var v2) ? v2.ToString()
        : string.Empty;

    /// <summary>Reconstruye la URL PÚBLICA exacta a la que el proveedor hizo POST (esquema+host+path+query).
    /// Twilio firma contra ella. Detrás del Gateway/túnel, se toma de X-Forwarded-Proto/Host; si no vienen,
    /// se cae a los del request. Proveedores que firman solo el body ignoran esto.</summary>
    private string BuildPublicUrl()
    {
        var scheme =
            Request.Headers.TryGetValue("X-Forwarded-Proto", out var proto) && !string.IsNullOrWhiteSpace(proto)
                ? proto.ToString().Split(',')[0].Trim()
                : Request.Scheme;
        var host =
            Request.Headers.TryGetValue("X-Forwarded-Host", out var fhost) && !string.IsNullOrWhiteSpace(fhost)
                ? fhost.ToString().Split(',')[0].Trim()
                : Request.Host.Value;
        return $"{scheme}://{host}{Request.PathBase}{Request.Path}{Request.QueryString}";
    }
}
