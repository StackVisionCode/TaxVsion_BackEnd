using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Customer.Api.Requests;
using TaxVision.Customer.Application.Abstractions;
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
[Authorize(Roles = "TenantAdmin")]
public sealed class CustomerImportsController(IMessageBus bus) : ControllerBase
{
    private const int MaxUploadBytes = 10 * 1024 * 1024; // 10 MB; el handler tambien valida

    // ---------- POST /customers/imports ----------
    [HttpPost]
    [RequestSizeLimit(MaxUploadBytes)]
    [Consumes("multipart/form-data")]
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
        if (!TryGetTenantAndUser(out var tenantId, out var userId))
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
    [ProducesResponseType<CustomerImportAttemptResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        if (!TryGetTenantAndUser(out var tenantId, out _))
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
    [ProducesResponseType<IReadOnlyList<CustomerImportAttemptResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Search(
        [FromQuery] int page = 1,
        [FromQuery] int size = 20,
        CancellationToken ct = default
    )
    {
        if (!TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var list = await bus.InvokeAsync<IReadOnlyList<CustomerImportAttemptResponse>>(
            new SearchCustomerImportsQuery(tenantId, page, size),
            ct
        );
        return Ok(list);
    }

    // ---------- POST /customers/imports/{id}/cancel ----------
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        if (!TryGetTenantAndUser(out var tenantId, out var userId))
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
    [Produces("text/csv", "application/json")]
    public async Task GetReport(
        Guid id,
        [FromServices] ICustomerImportReadService reader,
        [FromQuery] string format = "csv",
        CancellationToken ct = default
    )
    {
        if (!TryGetTenantAndUser(out var tenantId, out _))
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
            await writer.WriteLineAsync("RowNumber,Status,ResultingCustomerId,DisplayName,MatchedBy,ErrorCode,Message");

            await foreach (var row in reader.StreamRowsAsync(id, ct))
            {
                await writer.WriteLineAsync(
                    $"{row.RowNumber},{row.Status},{row.ResultingCustomerId},"
                        + $"\"{Escape(row.DisplayName)}\",\"{Escape(row.MatchedBy)}\","
                        + $"\"{Escape(row.ErrorCode)}\",\"{Escape(row.Message)}\""
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

    // ---------- GET /customers/imports/template ----------
    [HttpGet("template")]
    [Produces("text/csv")]
    public IActionResult GetTemplate()
    {
        var headers =
            "Kind,FirstName,MiddleName,LastName,Prefix,Suffix,LegalName,Dba,BusinessStructure,"
            + "FormationDate,PrincipalBusinessActivityCode,DateOfBirth,OccupationName,Email,Phone,"
            + "Language,PreferredChannel,AddressLine1,AddressLine2,City,Region,PostalCode,CountryCode,"
            + "TaxIdentifier,FilingStatus,PriorYearAgi,IsReturningCustomer,SpouseFirstName,SpouseMiddleName,"
            + "SpouseLastName,SpouseDateOfBirth,SpouseEmail,SpousePhone,SpouseTaxIdentifier";

        // Telefonos en formato E.164. El import tambien acepta formatos comunes US como
        // (305) 555-1234 o 305-555-1234, pero la plantilla muestra siempre el canonico.
        var exampleIndividual =
            "Individual,John,A,Doe,Mr,,,,,,,1985-03-15,Accountant,john.doe@example.com,"
            + "+13055551234,En,Email,123 Main St,,Miami,FL,33101,US,123-45-6789,Single,45000.00,"
            + "false,Jane,B,Doe,1987-07-22,jane.doe@example.com,+13057654321,987-65-4321";

        var exampleBusiness =
            "Business,,,,,,Acme LLC,Acme Inc,Llc,2020-01-15,541211,,"
            + ",contact@acme.com,+13055551111,En,Email,500 Brickell Ave,Suite 200,Miami,FL,33131,US,"
            + "12-3456789,,,,,,,,,,";

        var content = $"{headers}\n{exampleIndividual}\n{exampleBusiness}\n";
        return File(Encoding.UTF8.GetBytes(content), "text/csv", "customer-import-template.csv");
    }

    // ============ Helpers ============

    private bool TryGetTenantAndUser(out Guid tenantId, out Guid userId)
    {
        tenantId = Guid.Empty;
        userId = Guid.Empty;

        var tidClaim = User.FindFirst("tenant_id")?.Value;
        var subClaim =
            User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(tidClaim) || !Guid.TryParse(tidClaim, out tenantId))
            return false;
        if (string.IsNullOrEmpty(subClaim) || !Guid.TryParse(subClaim, out userId))
            return false;
        return true;
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return value.Replace("\"", "\"\"").Replace("\n", " ").Replace("\r", " ");
    }
}
