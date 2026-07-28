using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Documents.Api.Common;
using TaxVision.Documents.Application.Generations.GenerateInvoiceDocument;
using Wolverine;

namespace TaxVision.Documents.Api.Controllers;

/// <summary>
/// API M2M interna de Documents. Por diseño no está bajo /documents del Gateway. El tenant y el actor
/// salen del JWT de servicio (audience taxvision-documents). SCAFFOLD: solo el registro de generación
/// de factura como placeholder; el catálogo completo (status/retry/cancel/batch) llega por fases.
/// </summary>
[ApiController]
[Route("internal/document-generations")]
[Authorize(Policy = "ServiceOnly")]
[BuildingBlocks.ActorTypeAuthorization.AllowActorTypes(BuildingBlocks.ActorTypeAuthorization.ActorType.Service)]
public sealed class InternalDocumentGenerationsController(IMessageBus bus) : ControllerBase
{
    public sealed record GenerateInvoiceRequest(
        Guid InvoiceId,
        string InvoiceNumber,
        int DocumentVersion,
        string TemplateKey,
        int TemplateVersion,
        int TaxYear,
        InvoicePayload Invoice,
        BrandingPayload? Branding = null
    );

    /// <summary>Registra una solicitud de generación y devuelve 202 — la generación real es asíncrona.</summary>
    [HttpPost("invoices")]
    [ProducesResponseType<GenerateInvoiceDocumentResult>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> GenerateInvoice(
        GenerateInvoiceRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        [FromHeader(Name = "X-Correlation-Id")] string? correlationId,
        CancellationToken ct
    )
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<GenerateInvoiceDocumentResult>>(
            new GenerateInvoiceDocumentCommand(
                tenantId,
                request.InvoiceId,
                request.InvoiceNumber,
                request.DocumentVersion,
                request.TemplateKey,
                request.TemplateVersion,
                request.TaxYear,
                SourceService: "billing",
                idempotencyKey,
                correlationId ?? string.Empty,
                request.Invoice,
                request.Branding
            ),
            ct
        );

        return result.IsSuccess
            ? Accepted(result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
