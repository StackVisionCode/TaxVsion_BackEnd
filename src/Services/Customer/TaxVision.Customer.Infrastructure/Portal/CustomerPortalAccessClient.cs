using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Customer.Application.Abstractions;

namespace TaxVision.Customer.Infrastructure.Portal;

/// <summary>
/// Llama al endpoint M2M de Auth que crea o reenvía la invitación al portal. Los rechazos de Auth (409 y
/// demás) vuelven con su código y mensaje tal cual; token o red caídos se informan como transitorios.
/// </summary>
internal sealed class CustomerPortalAccessClient(
    HttpClient httpClient,
    IServiceTokenAcquirer tokenAcquirer,
    ILogger<CustomerPortalAccessClient> logger
) : ICustomerPortalAccessClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly Error Unavailable = new(
        "Customer.PortalAccessUnavailable",
        "We couldn't send the portal invitation right now. Try again in a moment."
    );

    public async Task<Result<PortalInvitationOutcome>> IssueInvitationAsync(
        Guid tenantId,
        Guid customerId,
        string email,
        Guid requestedByUserId,
        CancellationToken ct = default
    )
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        if (string.IsNullOrEmpty(token))
            return Result.Failure<PortalInvitationOutcome>(Unavailable);

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"internal/tenants/{tenantId:D}/customers/{customerId:D}/portal-invitation"
            )
            {
                Content = JsonContent.Create(new { email, requestedByUserId }, options: Json),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClient.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                var outcome = await response.Content.ReadFromJsonAsync<PortalInvitationOutcome>(Json, ct);
                return outcome is null ? Result.Failure<PortalInvitationOutcome>(Unavailable) : Result.Success(outcome);
            }

            if ((int)response.StatusCode < 500 && await ReadErrorAsync(response, ct) is { } rejection)
                return Result.Failure<PortalInvitationOutcome>(rejection);

            logger.LogWarning(
                "Portal invitation for customer {CustomerId} in tenant {TenantId} failed with {Status}.",
                customerId,
                tenantId,
                (int)response.StatusCode
            );
            return Result.Failure<PortalInvitationOutcome>(Unavailable);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Portal invitation call for customer {CustomerId} threw.", customerId);
            return Result.Failure<PortalInvitationOutcome>(Unavailable);
        }
    }

    private static async Task<Error?> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorBody>(Json, ct);
            return string.IsNullOrWhiteSpace(body?.Code) ? null : new Error(body.Code, body.Message ?? string.Empty);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record ErrorBody(string? Code, string? Message);
}
