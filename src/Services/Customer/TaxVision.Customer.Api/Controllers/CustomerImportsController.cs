using System.Text;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Customer.Api.Filters;
using TaxVision.Customer.Api.Requests;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Imports;
using TaxVision.Customer.Application.Imports.Commands.CancelCustomerImport;
using TaxVision.Customer.Application.Imports.Commands.StartCustomerImport;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Application.Imports.Queries.GetCustomerImportAttempt;
using TaxVision.Customer.Application.Imports.Queries.SearchCustomerImports;
using TaxVision.Customer.Domain.Imports;
using Wolverine;

namespace TaxVision.Customer.Api.Controllers;

[ApiController]
[Route("customers/imports")]
[Authorize]
// Quien decide es el PERMISO, no el tipo de cuenta. `customers.import` sigue fuera del bundle del
// empleado —no lo trae por defecto—, pero un administrador puede concederlo con un rol custom y a
// partir de ahí ese empleado importa. Antes el tipo de cuenta lo bloqueaba de todos modos: darle
// el permiso no servía de nada y el 403 no explicaba por qué.
//
// El gate sigue siendo de clase a propósito: quien puede importar necesita además ver el estado y
// el informe de SU importación, que son los otros endpoints de acá.
[HasPermission(CustomersPermissions.Import)]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class CustomerImportsController(IMessageBus bus) : ControllerBase
{
    // ---------- POST /customers/imports ----------
    [HttpPost]
    [ConfigurableImportSizeLimit] // limite de tamano tomado de CustomerImportOptions; el handler tambien valida
    [Consumes("multipart/form-data")]
    [RateLimit("customer.i.imports")]
    [ProducesResponseType<CustomerImportAttemptResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<Error>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Start(
        [FromForm] StartCustomerImportRequest body,
        IFormFile file,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return BadRequest(new Error("Import.IdempotencyKey", "Idempotency-Key header is required."));

        if (file is null || file.Length == 0)
            return BadRequest(new Error("Import.File", "File is required."));

        // Detectar formato por extension
        var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant();
        ImportSourceKind sourceKind = ext switch
        {
            ".csv" => ImportSourceKind.Csv,
            ".xlsx" => ImportSourceKind.Xlsx,
            _ => default,
        };
        if (ext != ".csv" && ext != ".xlsx")
            return BadRequest(new Error("Import.Format", "Only .csv and .xlsx files are supported."));

        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms, ct);
            bytes = ms.ToArray();
        }

        var cmd = new StartCustomerImportCommand(
            tenantId,
            userId,
            idempotencyKey.Trim(),
            body.Strategy,
            sourceKind,
            file.FileName,
            bytes
        );

        var result = await bus.InvokeAsync<Result<CustomerImportAttemptResponse>>(cmd, ct);

        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);

        return AcceptedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value);
    }

    // ---------- GET /customers/imports/{id} ----------
    [HttpGet("{id:guid}")]
    [RateLimit("customer.f.imports_get")]
    [ProducesResponseType<CustomerImportAttemptResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<CustomerImportAttemptResponse>>(
            new GetCustomerImportAttemptQuery(tenantId, id),
            ct
        );

        if (result.IsFailure)
            return NotFound(result.Error);

        return Ok(result.Value);
    }

    // ---------- GET /customers/imports ----------
    [HttpGet]
    [RateLimit("customer.f.imports_list")]
    [ProducesResponseType<IReadOnlyList<CustomerImportAttemptResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Search(
        [FromQuery] int page = 1,
        [FromQuery] int size = 20,
        CancellationToken ct = default
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var list = await bus.InvokeAsync<IReadOnlyList<CustomerImportAttemptResponse>>(
            new SearchCustomerImportsQuery(tenantId, page, size),
            ct
        );
        return Ok(list);
    }

    // ---------- POST /customers/imports/{id}/cancel ----------
    [HttpPost("{id:guid}/cancel")]
    [RateLimit("customer.g.imports_cancel")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new CancelCustomerImportCommand(tenantId, id, userId), ct);

        if (result.IsFailure)
            return result.Error.Code == "Import.NotFound"
                ? NotFound(result.Error)
                : StatusCode(result.Error.ToHttpStatusCode(), result.Error);

        return Accepted();
    }

    // ---------- GET /customers/imports/{id}/report?format=csv ----------
    [HttpGet("{id:guid}/report")]
    [RateLimit("customer.h.imports_report")]
    [Produces("text/csv", "application/json")]
    public async Task GetReport(
        Guid id,
        [FromServices] ICustomerImportReadService reader,
        [FromQuery] string format = "csv",
        CancellationToken ct = default
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        // Validar ownership via attempt
        var attempt = await reader.GetByIdAsync(id, ct);
        if (attempt is null || attempt.TenantId != tenantId)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (format.Equals("csv", StringComparison.OrdinalIgnoreCase))
        {
            Response.ContentType = "text/csv";
            Response.Headers.ContentDisposition = $"attachment; filename=\"import-{id}.csv\"";

            await using var writer = new StreamWriter(Response.Body, Encoding.UTF8);
            await writer.WriteLineAsync(
                "RowNumber,Status,ResultingCustomerId,DisplayName,MatchedBy,ErrorCode,Message,Warnings"
            );

            await foreach (var row in reader.StreamRowsAsync(id, ct))
            {
                await writer.WriteLineAsync(
                    $"{row.RowNumber},{row.Status},{row.ResultingCustomerId},"
                        + $"{ImportReportCsv.Cell(row.DisplayName)},{ImportReportCsv.Cell(row.MatchedBy)},"
                        + $"{ImportReportCsv.Cell(row.ErrorCode)},{ImportReportCsv.Cell(row.Message)},"
                        + ImportReportCsv.Cell(row.Warnings is null ? null : string.Join("; ", row.Warnings))
                );
            }
            await writer.FlushAsync();
            return;
        }

        // JSON streaming
        Response.ContentType = "application/json";
        await Response.WriteAsync("[", ct);
        var first = true;
        await foreach (var row in reader.StreamRowsAsync(id, ct))
        {
            if (!first)
                await Response.WriteAsync(",", ct);
            first = false;
            var json = System.Text.Json.JsonSerializer.Serialize(row);
            await Response.WriteAsync(json, ct);
        }
        await Response.WriteAsync("]", ct);
    }

    // ---------- GET /customers/imports/template?format=csv|xlsx ----------
    // CSV por defecto (lo que ya usaban los clientes); xlsx trae instrucciones y listas con el catálogo de ocupaciones.
    [HttpGet("template")]
    [RateLimit("customer.f.get")]
    [Produces("text/csv", XlsxContentType)]
    public async Task<IActionResult> GetTemplate(
        [FromQuery] string? format,
        [FromServices] ICustomerImportXlsxTemplate xlsxTemplate,
        CancellationToken ct
    )
    {
        if (!string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase))
            return File(
                Encoding.UTF8.GetBytes(CustomerImportTemplate.Csv),
                "text/csv",
                CustomerImportTemplate.FileName
            );

        var bytes = await xlsxTemplate.BuildAsync(ct);
        return File(bytes, XlsxContentType, CustomerImportTemplate.XlsxFileName);
    }

    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}
