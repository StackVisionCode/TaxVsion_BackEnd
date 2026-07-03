using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.Enrollments.Commands;
using Wolverine;

namespace TaxVision.Subscription.Api.Controllers;

[ApiController]
[Route("enrollments")]
public sealed class EnrollmentsController(IMessageBus bus) : ControllerBase
{
    /// <summary>
    /// Endpoint público — sin autenticación.
    /// El cliente selecciona el plan y paga antes de que exista el tenant.
    /// Devuelve EnrollmentId. El link de pago llega vía Payment Service.
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    [ProducesResponseType<CreateEnrollmentResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateEnrollmentCommand command,
        CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<CreateEnrollmentResponse>>(command, ct);

        return result.IsSuccess
            ? Accepted(result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
