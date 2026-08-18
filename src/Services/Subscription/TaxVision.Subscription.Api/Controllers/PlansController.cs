using BuildingBlocks.Results;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.Plans.Queries;
using Wolverine;

namespace TaxVision.Subscription.Api.Controllers;

[ApiController]
[Route("plans")]
public sealed class PlansController(IMessageBus bus) : ControllerBase
{
    /// <summary>
    /// Catálogo público de planes para la landing page. Fase 4.10 (rate limiting) — D-category
    /// exempt: [AllowAnonymous], sin JWT, sin limiter nativo previo que preservar. Agregar
    /// protección nueva queda fuera de alcance de una migración (mismo criterio que
    /// JwksController.Jwks de Signature).
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [ResponseCache(Duration = 300)]
    [RateLimitExempt(
        "Public plan catalog for the landing page — no JWT, no pre-existing native limiter; adding new protection is out of scope for a migration phase."
    )]
    [ProducesResponseType<IReadOnlyList<PlanResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPlans(CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<IReadOnlyList<PlanResponse>>>(new GetPlansQuery(), ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
