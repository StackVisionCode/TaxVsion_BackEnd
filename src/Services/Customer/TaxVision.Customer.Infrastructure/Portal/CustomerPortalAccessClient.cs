using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Customer.Application.Abstractions;

namespace TaxVision.Customer.Infrastructure.Portal;

/// <summary>
/// Llama a los endpoints M2M de Auth del acceso al portal (invitación, segundo factor y link de reset). Los rechazos de
/// Auth (409 y demás) vuelven con su código y mensaje tal cual; token o red caídos se informan como transitorios.
/// Las acciones sobre la credencial, el MFA, las sesiones y la invitación reenvían además el token del actor
/// (SR-03, SR-11).
/// </summary>
internal sealed class CustomerPortalAccessClient(
    HttpClient httpClient,
    IServiceTokenAcquirer tokenAcquirer,
    IActorAccessTokenReader actorToken,
    ILogger<CustomerPortalAccessClient> logger
) : ICustomerPortalAccessClient, ICustomerPortalSessionsClient, ICustomerPortalCredentialsClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly Error InvitationUnavailable = new(
        "Customer.PortalAccessUnavailable",
        "We couldn't send the portal invitation right now. Try again in a moment."
    );

    private static readonly Error SecurityUnavailable = new(
        "Customer.PortalAccessUnavailable",
        "We couldn't reach the portal sign-in service right now. Try again in a moment."
    );

    public Task<Result<PortalInvitationOutcome>> IssueInvitationAsync(
        Guid tenantId,
        Guid customerId,
        string email,
        string? firstName,
        string? lastName,
        Guid requestedByUserId,
        CancellationToken ct = default
    ) =>
        SendAsync<PortalInvitationOutcome>(
            tenantId,
            customerId,
            HttpMethod.Post,
            "portal-invitation",
            new
            {
                email,
                requestedByUserId,
                firstName,
                lastName,
            },
            InvitationUnavailable,
            ct,
            forwardActor: true
        );

    public Task<Result<PortalMfaStatus>> GetMfaStatusAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct = default
    ) => SendAsync<PortalMfaStatus>(tenantId, customerId, HttpMethod.Get, "portal-mfa", null, SecurityUnavailable, ct);

    public Task<Result<PortalMfaResetOutcome>> ResetMfaAsync(
        Guid tenantId,
        Guid customerId,
        PortalCredentialRequest request,
        CancellationToken ct = default
    ) =>
        SendAsync<PortalMfaResetOutcome>(
            tenantId,
            customerId,
            HttpMethod.Post,
            "portal-mfa/reset",
            request,
            SecurityUnavailable,
            ct,
            forwardActor: true
        );

    public Task<Result<PortalSessionPage>> ListSessionsAsync(
        Guid tenantId,
        Guid customerId,
        PortalSessionsQuery query,
        CancellationToken ct = default
    )
    {
        var parameters = new List<string>();
        if (query.FromUtc is { } from)
            parameters.Add($"from={Uri.EscapeDataString(from.ToString("O"))}");
        if (query.ToUtc is { } to)
            parameters.Add($"to={Uri.EscapeDataString(to.ToString("O"))}");
        if (query.Page is { } page)
            parameters.Add($"page={page}");
        if (query.PageSize is { } pageSize)
            parameters.Add($"pageSize={pageSize}");
        var resource = parameters.Count == 0 ? "portal-sessions" : $"portal-sessions?{string.Join('&', parameters)}";
        return SendAsync<PortalSessionPage>(
            tenantId,
            customerId,
            HttpMethod.Get,
            resource,
            null,
            SecurityUnavailable,
            ct,
            forwardActor: true
        );
    }

    public Task<Result> RevokeSessionAsync(
        Guid tenantId,
        Guid customerId,
        Guid sessionId,
        Guid requestedByUserId,
        CancellationToken ct = default
    ) =>
        SendWithoutContentAsync(
            tenantId,
            customerId,
            $"portal-sessions/{sessionId:D}?requestedByUserId={requestedByUserId:D}",
            ct,
            forwardActor: true
        );

    public Task<Result> RevokeAllSessionsAsync(
        Guid tenantId,
        Guid customerId,
        Guid requestedByUserId,
        bool forgetTrustedDevices,
        CancellationToken ct = default
    ) =>
        SendWithoutContentAsync(
            tenantId,
            customerId,
            $"portal-sessions?requestedByUserId={requestedByUserId:D}&forgetTrustedDevices={(forgetTrustedDevices ? "true" : "false")}",
            ct,
            forwardActor: true
        );

    public Task<Result<PortalCredentialStatusResponse>> GetStatusAsync(
        Guid tenantId,
        Guid customerId,
        Guid requestedByUserId,
        CancellationToken ct = default
    ) =>
        SendAsync<PortalCredentialStatusResponse>(
            tenantId,
            customerId,
            HttpMethod.Get,
            $"portal-credentials?requestedByUserId={requestedByUserId:D}",
            null,
            SecurityUnavailable,
            ct,
            forwardActor: true
        );

    public Task<Result<ResetLinkIssuedResponse>> SendResetLinkAsync(
        Guid tenantId,
        Guid customerId,
        PortalCredentialRequest request,
        CancellationToken ct = default
    ) =>
        SendAsync<ResetLinkIssuedResponse>(
            tenantId,
            customerId,
            HttpMethod.Post,
            "portal-credentials/reset-link",
            request,
            SecurityUnavailable,
            ct,
            forwardActor: true
        );

    public Task<Result> CancelResetLinkAsync(
        Guid tenantId,
        Guid customerId,
        PortalCredentialRequest request,
        CancellationToken ct = default
    ) =>
        SendWithoutContentAsync(tenantId, customerId, "portal-credentials/reset-link", ct, request, forwardActor: true);

    public Task<Result<CredentialStateChangedResponse>> RequireChangeAsync(
        Guid tenantId,
        Guid customerId,
        PortalCredentialRequest request,
        CancellationToken ct = default
    ) =>
        SendAsync<CredentialStateChangedResponse>(
            tenantId,
            customerId,
            HttpMethod.Post,
            "portal-credentials/require-change",
            request,
            SecurityUnavailable,
            ct,
            forwardActor: true
        );

    public Task<Result<AccountSecuredResponse>> SecureAsync(
        Guid tenantId,
        Guid customerId,
        PortalCredentialRequest request,
        CancellationToken ct = default
    ) =>
        SendAsync<AccountSecuredResponse>(
            tenantId,
            customerId,
            HttpMethod.Post,
            "portal-credentials/secure",
            request,
            SecurityUnavailable,
            ct,
            forwardActor: true
        );

    /// <summary>DELETE que responde 204: mismo trato de errores que <see cref="SendAsync{T}"/>.</summary>
    private async Task<Result> SendWithoutContentAsync(
        Guid tenantId,
        Guid customerId,
        string resource,
        CancellationToken ct,
        object? body = null,
        bool forwardActor = false
    )
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        if (string.IsNullOrEmpty(token))
            return Result.Failure(SecurityUnavailable);

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Delete,
                $"internal/tenants/{tenantId:D}/customers/{customerId:D}/{resource}"
            );
            if (body is not null)
                request.Content = JsonContent.Create(body, options: Json);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (forwardActor)
                AttachActorToken(request);
            using var response = await httpClient.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
                return Result.Success();
            if ((int)response.StatusCode < 500 && await ReadErrorAsync(response, ct) is { } rejection)
                return Result.Failure(rejection);
            logger.LogWarning(
                "Portal call {Resource} for customer {CustomerId} in tenant {TenantId} failed with {Status}.",
                resource.Split('?')[0],
                customerId,
                tenantId,
                (int)response.StatusCode
            );
            return Result.Failure(SecurityUnavailable);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Portal sessions call for customer {CustomerId} threw.", customerId);
            return Result.Failure(SecurityUnavailable);
        }
    }

    private async Task<Result<T>> SendAsync<T>(
        Guid tenantId,
        Guid customerId,
        HttpMethod method,
        string resource,
        object? body,
        Error unavailable,
        CancellationToken ct,
        bool forwardActor = false
    )
        where T : class
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        if (string.IsNullOrEmpty(token))
            return Result.Failure<T>(unavailable);

        try
        {
            using var request = new HttpRequestMessage(
                method,
                $"internal/tenants/{tenantId:D}/customers/{customerId:D}/{resource}"
            );
            if (body is not null)
                request.Content = JsonContent.Create(body, options: Json);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (forwardActor)
                AttachActorToken(request);

            using var response = await httpClient.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                var value = await response.Content.ReadFromJsonAsync<T>(Json, ct);
                return value is null ? Result.Failure<T>(unavailable) : Result.Success(value);
            }

            if ((int)response.StatusCode < 500 && await ReadErrorAsync(response, ct) is { } rejection)
                return Result.Failure<T>(rejection);

            logger.LogWarning(
                "Portal access call {Resource} for customer {CustomerId} in tenant {TenantId} failed with {Status}.",
                resource,
                customerId,
                tenantId,
                (int)response.StatusCode
            );
            return Result.Failure<T>(unavailable);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(
                ex,
                "Portal access call {Resource} for customer {CustomerId} threw.",
                resource,
                customerId
            );
            return Result.Failure<T>(unavailable);
        }
    }

    // Sin token del actor no se manda la cabecera: Auth responde ActorTokenInvalid.
    private void AttachActorToken(HttpRequestMessage request)
    {
        if (actorToken.Current is { Length: > 0 } token)
            request.Headers.TryAddWithoutValidation(ForwardedActorToken.HeaderName, $"Bearer {token}");
    }

    private static async Task<Error?> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorBody>(Json, ct);
            // La espera del 429 por objetivo (retryAfterSeconds) también vuelve al CRM.
            return string.IsNullOrWhiteSpace(body?.Code)
                ? null
                : new Error(body.Code, body.Message ?? string.Empty) { RetryAfterSeconds = body.RetryAfterSeconds };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record ErrorBody(string? Code, string? Message, int? RetryAfterSeconds = null);
}
