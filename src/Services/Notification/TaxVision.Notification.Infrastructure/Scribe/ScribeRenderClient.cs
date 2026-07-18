using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;

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

    public async Task<Result<ScribeRenderedEmail>> RenderAsync(
        string eventKey,
        Guid tenantId,
        IReadOnlyDictionary<string, object?> variables,
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
        request.Content = JsonContent.Create(new RenderRequestDto(eventKey, tenantId, variables), options: Json);

        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Scribe render call for event '{EventKey}' failed ({Status}).",
                eventKey,
                (int)response.StatusCode
            );
            return Result.Failure<ScribeRenderedEmail>(
                new Error("Email.ScribeRender", $"Scribe render request failed ({(int)response.StatusCode}).")
            );
        }

        var payload = await response.Content.ReadFromJsonAsync<RenderResponseDto>(Json, ct);
        if (payload is null)
            return Result.Failure<ScribeRenderedEmail>(new Error("Email.ScribeRender", "Empty Scribe response."));

        return Result.Success(
            new ScribeRenderedEmail(payload.Subject, payload.Html, payload.Text, payload.InlineAssets ?? [])
        );
    }

    private sealed record RenderRequestDto(
        string EventKey,
        Guid? TenantId,
        IReadOnlyDictionary<string, object?> Variables
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
        IReadOnlyList<EmailInlineAssetReference>? InlineAssets
    );
}
