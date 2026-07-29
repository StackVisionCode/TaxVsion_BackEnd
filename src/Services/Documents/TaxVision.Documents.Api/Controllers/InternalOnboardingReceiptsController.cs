using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Documents.Application.Generations.OnboardingReceipt;
using Wolverine;

namespace TaxVision.Documents.Api.Controllers;

/// <summary>PayFlow (Fase 10) — M2M-only: Auth (Fase 11) invoca este endpoint tras confirmar el
/// pago de un onboarding, para generar el PDF del recibo. No hay tenant en la request — el caller
/// (Auth) tampoco tiene uno para este onboarding todavía; la generación se registra internamente
/// bajo el tenant plataforma (ver GenerateOnboardingReceiptDocumentHandler).</summary>
[ApiController]
[Route("internal/document-generations/onboarding-receipts")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalOnboardingReceiptsController(IMessageBus bus) : ControllerBase
{
    public sealed record GenerateOnboardingReceiptRequest(
        Guid OnboardingId,
        int DocumentVersion,
        string TemplateKey,
        int TemplateVersion,
        OnboardingReceiptPayload Receipt
    );

    [HttpPost]
    [ProducesResponseType<GenerateOnboardingReceiptDocumentResult>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Generate(
        GenerateOnboardingReceiptRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        [FromHeader(Name = "X-Correlation-Id")] string? correlationId,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<Result<GenerateOnboardingReceiptDocumentResult>>(
            new GenerateOnboardingReceiptDocumentCommand(
                request.OnboardingId,
                request.DocumentVersion,
                request.TemplateKey,
                request.TemplateVersion,
                SourceService: "auth",
                idempotencyKey,
                correlationId ?? string.Empty,
                request.Receipt
            ),
            ct
        );

        return result.IsSuccess ? Accepted(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
