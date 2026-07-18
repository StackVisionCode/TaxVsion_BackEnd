using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Infrastructure.Customers;

namespace TaxVision.Correspondence.Infrastructure.Connectors;

/// <summary>
/// Implementación de <see cref="IConnectorsClient"/> contra
/// <c>POST /connectors/messages/{providerMessageId}/body</c> de Connectors.Api (policy
/// ServiceOnly, Connectors Fase 8). Timeout total 30s (fijado en el <see cref="HttpClient"/>,
/// ver <c>AddCorrespondenceInfrastructure</c>), retry 1× con backoff fijo ante fallas
/// transitorias (excepción de red/timeout, o HTTP 5xx sin cuerpo de error estructurado) —
/// nunca reintenta un 4xx estructurado (403/429), reintentar eso no cambia el resultado.
/// </summary>
internal sealed class ConnectorsClient(
    HttpClient httpClient,
    ICorrespondenceServiceTokenAcquirer tokenAcquirer,
    ILogger<ConnectorsClient> logger
) : IConnectorsClient
{
    private static readonly TimeSpan RetryBackoff = TimeSpan.FromMilliseconds(500);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<Result<MessageBodyResponse>> FetchMessageBodyAsync(
        Guid tenantId,
        Guid accountId,
        string providerMessageId,
        CancellationToken ct = default
    )
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        if (string.IsNullOrEmpty(token))
            return Result.Failure<MessageBodyResponse>(
                new Error(
                    "ConnectorsClient.ServiceAuthUnavailable",
                    "Could not acquire a service token to call Connectors."
                )
            );

        var first = await SendOnceAsync(token, tenantId, accountId, providerMessageId, ct);
        if (!first.IsTransient)
            return first.Result;

        logger.LogWarning(
            "Connectors body fetch failed transiently for message {ProviderMessageId}; retrying once.",
            providerMessageId
        );
        await Task.Delay(RetryBackoff, ct);

        var second = await SendOnceAsync(token, tenantId, accountId, providerMessageId, ct);
        return second.IsTransient
            ? Result.Failure<MessageBodyResponse>(
                new Error("ConnectorsClient.Unavailable", "Connectors did not respond after retrying.")
            )
            : second.Result;
    }

    private async Task<Attempt> SendOnceAsync(
        string token,
        Guid tenantId,
        Guid accountId,
        string providerMessageId,
        CancellationToken ct
    )
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"connectors/messages/{Uri.EscapeDataString(providerMessageId)}/body"
            )
            {
                Content = JsonContent.Create(new { tenantId, accountId }, options: Json),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClient.SendAsync(request, ct);
            return response.IsSuccessStatusCode
                ? await ReadSuccessAsync(response, ct)
                : await ReadFailureAsync(response, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Connectors body fetch threw for message {ProviderMessageId}.", providerMessageId);
            return new Attempt(
                IsTransient: true,
                Result.Failure<MessageBodyResponse>(new Error("ConnectorsClient.RequestFailed", ex.Message))
            );
        }
    }

    private static async Task<Attempt> ReadSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        ConnectorsMessageBodyDto? body;
        try
        {
            body = await response.Content.ReadFromJsonAsync<ConnectorsMessageBodyDto>(Json, ct);
        }
        catch (JsonException)
        {
            body = null;
        }

        if (body is null)
            return new Attempt(
                IsTransient: true,
                Result.Failure<MessageBodyResponse>(
                    new Error("ConnectorsClient.EmptyResponse", "Connectors returned an unreadable body response.")
                )
            );

        var headers = body.Headers ?? new Dictionary<string, string>();
        return new Attempt(
            IsTransient: false,
            Result.Success(new MessageBodyResponse(body.HtmlBody, body.TextBody, headers))
        );
    }

    /// <summary>
    /// Un 4xx con <see cref="Error"/> estructurado (403 Forbidden, 429 RateLimited) es una falla
    /// de negocio real — no transitoria, no se reintenta. Un 5xx (502 ProviderFailed, 504
    /// Timeout) o una respuesta sin cuerpo de error interpretable sí se trata como transitoria.
    /// </summary>
    private static async Task<Attempt> ReadFailureAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var error = await TryReadErrorAsync(response, ct);
        var isTransient = error is null || (int)response.StatusCode >= 500;
        return new Attempt(
            isTransient,
            Result.Failure<MessageBodyResponse>(
                error
                    ?? new Error(
                        "ConnectorsClient.UnexpectedStatus",
                        $"Connectors returned HTTP {(int)response.StatusCode}."
                    )
            )
        );
    }

    private static async Task<Error?> TryReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<Error>(Json, ct);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<Result<ConnectorsAttachmentBytes>> FetchAttachmentAsync(
        Guid tenantId,
        Guid accountId,
        string providerMessageId,
        string providerAttachmentId,
        CancellationToken ct = default
    )
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        if (string.IsNullOrEmpty(token))
            return Result.Failure<ConnectorsAttachmentBytes>(
                new Error(
                    "ConnectorsClient.ServiceAuthUnavailable",
                    "Could not acquire a service token to call Connectors."
                )
            );

        var first = await SendAttachmentOnceAsync(
            token,
            tenantId,
            accountId,
            providerMessageId,
            providerAttachmentId,
            ct
        );
        if (!first.IsTransient)
            return first.Result;

        logger.LogWarning(
            "Connectors attachment fetch failed transiently for attachment {ProviderAttachmentId}; retrying once.",
            providerAttachmentId
        );
        await Task.Delay(RetryBackoff, ct);

        var second = await SendAttachmentOnceAsync(
            token,
            tenantId,
            accountId,
            providerMessageId,
            providerAttachmentId,
            ct
        );
        return second.IsTransient
            ? Result.Failure<ConnectorsAttachmentBytes>(
                new Error("ConnectorsClient.Unavailable", "Connectors did not respond after retrying.")
            )
            : second.Result;
    }

    private async Task<AttachmentAttempt> SendAttachmentOnceAsync(
        string token,
        Guid tenantId,
        Guid accountId,
        string providerMessageId,
        string providerAttachmentId,
        CancellationToken ct
    )
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"connectors/messages/{Uri.EscapeDataString(providerMessageId)}/attachments/{Uri.EscapeDataString(providerAttachmentId)}"
            )
            {
                Content = JsonContent.Create(new { tenantId, accountId }, options: Json),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return await ReadAttachmentFailureAsync(response, ct);

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            return new AttachmentAttempt(IsTransient: false, Result.Success(new ConnectorsAttachmentBytes(bytes)));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(
                ex,
                "Connectors attachment fetch threw for attachment {ProviderAttachmentId}.",
                providerAttachmentId
            );
            return new AttachmentAttempt(
                IsTransient: true,
                Result.Failure<ConnectorsAttachmentBytes>(new Error("ConnectorsClient.RequestFailed", ex.Message))
            );
        }
    }

    /// <summary>Mismo criterio que <see cref="ReadFailureAsync"/>: 4xx estructurado no es transitorio, 5xx/sin cuerpo interpretable sí.</summary>
    private static async Task<AttachmentAttempt> ReadAttachmentFailureAsync(
        HttpResponseMessage response,
        CancellationToken ct
    )
    {
        var error = await TryReadErrorAsync(response, ct);
        var isTransient = error is null || (int)response.StatusCode >= 500;
        return new AttachmentAttempt(
            isTransient,
            Result.Failure<ConnectorsAttachmentBytes>(
                error
                    ?? new Error(
                        "ConnectorsClient.UnexpectedStatus",
                        $"Connectors returned HTTP {(int)response.StatusCode}."
                    )
            )
        );
    }

    private readonly record struct Attempt(bool IsTransient, Result<MessageBodyResponse> Result);

    private readonly record struct AttachmentAttempt(bool IsTransient, Result<ConnectorsAttachmentBytes> Result);

    private sealed record ConnectorsMessageBodyDto(
        string? HtmlBody,
        string? TextBody,
        IReadOnlyDictionary<string, string>? Headers
    );
}
