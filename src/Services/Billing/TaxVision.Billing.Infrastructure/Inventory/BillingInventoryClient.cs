using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Infrastructure.ServiceAuth;

namespace TaxVision.Billing.Infrastructure.Inventory;

/// <summary>
/// Cliente M2M hacia Inventory (POST internal/stock/commit-sale). Usa el token de servicio "Platform"
/// (audience amplia TaxVision.Services), el mismo que ya vale para endpoints que solo exigen
/// ActorType.Service. Semántica bloqueante: un 409 (stock insuficiente) o cualquier fallo/inalcanzable
/// devuelve Failure para que Billing NO emita la factura (fail-closed). El descuento es idempotente por
/// factura del lado de Inventory.
/// </summary>
public sealed class BillingInventoryClient(
    HttpClient http,
    IServiceTokenProvider tokenProvider,
    ILogger<BillingInventoryClient> logger
) : IInventoryStockClient
{
    public async Task<Result> CommitInvoiceSaleAsync(
        Guid tenantId,
        Guid invoiceId,
        IReadOnlyList<InvoiceSaleLine> lines,
        CancellationToken ct = default
    )
    {
        var token = await tokenProvider.GetTokenAsync(BillingServiceClientsOptions.PlatformClientName, tenantId, ct);
        if (string.IsNullOrEmpty(token))
            return Result.Failure(
                new Error("Billing.Inventory.TokenFailed", "Could not acquire a service token for Inventory.")
            );

        var body = new
        {
            invoiceId,
            lines = lines.Select(l => new { catalogItemId = l.CatalogItemId, quantity = l.Quantity }),
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "internal/stock/commit-sale")
        {
            Content = JsonContent.Create(body),
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await http.SendAsync(httpRequest, ct);
            if (response.IsSuccessStatusCode)
                return Result.Success();

            // 409 = stock insuficiente (dato del tenant, corregible): se propaga tal cual para el usuario.
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                var error = await ReadErrorAsync(response, ct);
                return Result.Failure(
                    error is { Code: not null, Message: not null }
                        ? new Error(error.Code, error.Message)
                        : new Error("inventory.insufficientStock", "Insufficient stock to issue this invoice.")
                );
            }

            var errorBody = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning(
                "Inventory commit-sale for invoice {InvoiceId} failed: {Status} {Body}",
                invoiceId,
                (int)response.StatusCode,
                errorBody
            );
            return Result.Failure(
                new Error("Billing.Inventory.CommitFailed", $"Inventory returned {(int)response.StatusCode}.")
            );
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Inventory commit-sale for invoice {InvoiceId} threw.", invoiceId);
            return Result.Failure(
                new Error("Billing.Inventory.Unreachable", "Inventory service is unreachable; cannot verify stock.")
            );
        }
    }

    private static async Task<ErrorDto?> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ErrorDto>(ct);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private sealed record ErrorDto(string? Code, string? Message);
}
