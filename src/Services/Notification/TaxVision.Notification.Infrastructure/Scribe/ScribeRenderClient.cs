using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;

namespace TaxVision.Notification.Infrastructure.Scribe;

/// <summary>
/// Cliente HTTP del microservicio Scribe (POST /scribe/render). Siempre corre en contexto background
/// (Wolverine consumer), así que solo usa el token M2M de <see cref="IServiceTokenAcquirer"/> — el
/// mismo acquirer ya usado para CloudStorage, reutilizable porque el token M2M no está atado a un
/// downstream específico (representa "notification-service actuando por el tenant X").
/// </summary>
public sealed class ScribeRenderClient(
    HttpClient httpClient,
    IServiceTokenAcquirer tokenAcquirer,
    ILogger<ScribeRenderClient> logger
) : IScribeRenderClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public Task<Result<ScribeRenderedEmail>> RenderAsync(
        string eventKey,
        Guid tenantId,
        IReadOnlyDictionary<string, object?> variables,
        CancellationToken ct = default
    ) => RenderAsync(eventKey, tenantId, variables, ScribeRenderAudience.Staff, ct);

    public async Task<Result<ScribeRenderedEmail>> RenderAsync(
        string eventKey,
        Guid tenantId,
        IReadOnlyDictionary<string, object?> variables,
        ScribeRenderAudience audience,
        CancellationToken ct = default
    )
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        if (string.IsNullOrEmpty(token))
            return Result.Failure<ScribeRenderedEmail>(
                new Error("Email.ScribeAuth", "No Scribe credentials available.")
            );

        using var request = new HttpRequestMessage(HttpMethod.Post, "scribe/render");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(
            new RenderRequestDto(eventKey, tenantId, variables, Audience: audience),
            options: Json
        );

        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return Result.Failure<ScribeRenderedEmail>(await ToErrorAsync(response, "Email.ScribeRender", eventKey, ct));

        var payload = await response.Content.ReadFromJsonAsync<RenderResponseDto>(Json, ct);
        if (payload is null)
            return Result.Failure<ScribeRenderedEmail>(new Error("Email.ScribeRender", "Empty Scribe response."));

        return Result.Success(
            new ScribeRenderedEmail(
                payload.Subject,
                payload.Html,
                payload.Text,
                payload.InlineAssets ?? [],
                ParseDispatchScope(payload.DispatchScope),
                payload.TemplateKey,
                payload.VersionNumber,
                payload.LayoutVersionNumber
            )
        );
    }

    public async Task<Result<ScribeRenderedText>> RenderTextAsync(
        string eventKey,
        Guid tenantId,
        IReadOnlyDictionary<string, object?> variables,
        CancellationToken ct = default
    )
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        if (string.IsNullOrEmpty(token))
            return Result.Failure<ScribeRenderedText>(
                new Error("Notification.ScribeAuth", "No Scribe credentials available.")
            );

        using var request = new HttpRequestMessage(HttpMethod.Post, "scribe/render");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(
            new RenderRequestDto(eventKey, tenantId, variables, "Push"),
            options: Json
        );
        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return Result.Failure<ScribeRenderedText>(
                await ToErrorAsync(response, "Notification.ScribeRender", eventKey, ct)
            );
        var payload = await response.Content.ReadFromJsonAsync<RenderResponseDto>(Json, ct);
        return payload?.Text is { } text
            ? Result.Success(new ScribeRenderedText(payload.Subject, text))
            : Result.Failure<ScribeRenderedText>(
                new Error("Notification.ScribeRender", "Scribe returned no push text.")
            );
    }

    /// <summary>
    /// SCR-11. Solo es rechazo un 400/404/422 con el código de error de Scribe en el cuerpo. Un 401, 403, 408, 409 o
    /// 429 depende del momento, y un 4xx sin código puede no venir de Scribe: esos se siguen reintentando.
    /// </summary>
    private async Task<Error> ToErrorAsync(
        HttpResponseMessage response,
        string failureCode,
        string eventKey,
        CancellationToken ct
    )
    {
        var status = (int)response.StatusCode;
        var scribeCode = await ReadErrorCodeAsync(response, ct);
        logger.LogWarning(
            "Scribe render call for event '{EventKey}' failed ({Status}, {ScribeCode}).",
            eventKey,
            status,
            scribeCode
        );

        return
            scribeCode is not null
            && response.StatusCode
                is HttpStatusCode.BadRequest
                    or HttpStatusCode.NotFound
                    or HttpStatusCode.UnprocessableEntity
            ? new Error(ScribeRenderRejectedException.ErrorCode, $"Scribe rejected the request ({status}, {scribeCode}).")
            : new Error(failureCode, $"Scribe render request failed ({status}).");
    }

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ErrorResponseDto>(Json, ct);
            return string.IsNullOrWhiteSpace(error?.Code) ? null : error.Code;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Scope desconocido o ausente = System. Un valor que no se entiende no puede terminar mandando el
    /// correo por el buzon de una oficina.
    /// </summary>
    private static EmailDispatchScope ParseDispatchScope(string? value) =>
        Enum.TryParse<EmailDispatchScope>(value, ignoreCase: true, out var parsed) ? parsed : EmailDispatchScope.System;

    private sealed record ErrorResponseDto(string? Code);

    // Audience viaja como texto ("Staff"/"Client") por el JsonStringEnumConverter.
    private sealed record RenderRequestDto(
        string EventKey,
        Guid? TenantId,
        IReadOnlyDictionary<string, object?> Variables,
        string Channel = "Email",
        ScribeRenderAudience Audience = ScribeRenderAudience.Staff
    );

    /// <summary>
    /// Hardening Fase 9: <c>InlineAssets</c> es el campo que faltaba acá — el endpoint real
    /// (<c>POST /scribe/render</c>, ver <c>RenderController</c> y
    /// <c>TaxVision.Scribe.Application.Rendering.RenderedContent</c>) siempre lo devolvió, pero este
    /// DTO solo declaraba Subject/Html/Text — <c>System.Text.Json</c> ignora en silencio cualquier
    /// propiedad del JSON que no tenga un miembro correspondiente en el tipo destino, así que ningún
    /// logo llegaba nunca más allá de este deserializador. <see cref="EmailInlineAssetReference"/> es
    /// el mismo shape exacto (ContentId/CloudStorageFileId/ContentType/SizeBytes) que Scribe serializa
    /// — se reusa el tipo de BuildingBlocks en vez de declarar un DTO paralelo porque este mismo valor
    /// viaja sin transformación hasta el evento que se publica más abajo.
    /// </summary>
    private sealed record RenderResponseDto(
        string Subject,
        string Html,
        string? Text,
        IReadOnlyList<EmailInlineAssetReference>? InlineAssets,
        string? DispatchScope,
        string? TemplateKey = null,
        int? VersionNumber = null,
        int? LayoutVersionNumber = null
    );
}
