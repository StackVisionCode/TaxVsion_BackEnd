using BuildingBlocks.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.Enrollments.Commands;
using TaxVision.Subscription.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Subscription.Api.Controllers;

[ApiController]
[Route("enrollments")]
public sealed class EnrollmentsController(IMessageBus bus) : ControllerBase
{
    public sealed record CreateEnrollmentRequest(
        string PlanCode,
        BillingPeriod BillingPeriod,
        string AdminEmail,
        string OrgName,
        string Subdomain,
        string TimeZoneId);

    /// <summary>
    /// Endpoint público — sin autenticación.
    /// El cliente selecciona el plan y paga antes de que exista el tenant.
    /// Devuelve EnrollmentId. El link de pago llega vía Payment Service.
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Create(
        [FromBody] CreateEnrollmentRequest req,
        CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<CreateEnrollmentResponse>>(
            new CreateEnrollmentCommand(
                req.PlanCode, req.BillingPeriod,
                req.AdminEmail, req.OrgName,
                req.Subdomain, req.TimeZoneId), ct);

        return result.IsSuccess
            ? Accepted(new { result.Value.EnrollmentId, result.Value.Status, result.Value.TotalAmount })
            : UnprocessableEntity(new { result.Error.Code, result.Error.Message });
    }
}
