using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Permissions;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Csv;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tasks.Api.Filters;
using TaxVision.Tasks.ClientForms.Application.Reporting;
using Wolverine;

namespace TaxVision.Tasks.Api.Controllers.ClientForms;

// P-12 Reporting. Los tres endpoints exigen tasks.form_requests.export y respetan el ABAC del actor
// (solo clientes visibles); el ABAC fino lo aplican las queries con el actor y CanViewAll.
[ApiController]
[ClientFormsEndpoint]
[Route("tasks/forms/reports")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
[HasPermission(TasksPermissions.FormRequestsExport)]
public sealed class FormReportsController(IMessageBus bus, IUserPermissionsSource permissions) : ControllerBase
{
    [HttpGet("keys")]
    [RateLimit("task.f.form_read")]
    public async Task<IActionResult> Keys([FromQuery] Guid templateId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Forbid();
        return ToActionResult(
            await bus.InvokeAsync<Result<FormReportKeysResponse>>(new FormReportKeysQuery(tenantId, templateId), ct)
        );
    }

    [HttpGet("answers")]
    [RateLimit("task.f.form_read")]
    public async Task<IActionResult> Answers(
        [FromQuery] Guid templateId,
        [FromQuery] string key,
        [FromQuery] string? value,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] int page = 1,
        CancellationToken ct = default
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        if (string.IsNullOrWhiteSpace(key))
            return BadRequest(KeyRequired);
        var result = await bus.InvokeAsync<BuildingBlocks.Common.PagedResult<FormReportAnswerRow>>(
            new FormReportAnswersQuery(
                tenantId,
                userId,
                await CanViewAllAsync(ct),
                templateId,
                key,
                value,
                from,
                to,
                page
            ),
            ct
        );
        return Ok(result);
    }

    [HttpGet("summary")]
    [RateLimit("task.f.form_read")]
    public async Task<IActionResult> Summary([FromQuery] Guid templateId, [FromQuery] string key, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        if (string.IsNullOrWhiteSpace(key))
            return BadRequest(KeyRequired);
        var result = await bus.InvokeAsync<FormReportSummaryResponse>(
            new FormReportSummaryQuery(tenantId, userId, await CanViewAllAsync(ct), templateId, key),
            ct
        );
        return Ok(result);
    }

    [HttpGet("export")]
    [RateLimit("task.h.form_report_export")]
    public async Task<IActionResult> Export(
        [FromQuery] Guid templateId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        var result = await bus.InvokeAsync<Result<FormReportExportResult>>(
            new FormReportExportQuery(tenantId, userId, await CanViewAllAsync(ct), templateId, from, to),
            ct
        );
        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);
        var bytes = CsvWriter.WriteWithBom(
            result.Value.Headers,
            result.Value.Rows.Select(row => (IReadOnlyList<string?>)row)
        );
        return File(bytes, "text/csv", result.Value.FileName);
    }

    private Task<bool> CanViewAllAsync(CancellationToken ct) =>
        permissions.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);

    private IActionResult ToActionResult<T>(Result<T> result) =>
        result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);

    private static readonly Error KeyRequired = new("FormReport.KeyRequired", "A reportable key is required.");
}
