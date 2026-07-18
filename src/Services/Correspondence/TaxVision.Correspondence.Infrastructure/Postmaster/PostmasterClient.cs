using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Infrastructure.Customers;
using TaxVision.Correspondence.Infrastructure.Observability;

namespace TaxVision.Correspondence.Infrastructure.Postmaster;

/// <summary>
/// Implementación de <see cref="IPostmasterClient"/> contra
/// <c>POST /postmaster/correspondence-messages</c> (policy <c>ServiceOnly</c>, D3 Compose §14/§15,
/// ya en producción del lado de Postmaster) — el tramo final de la cadena síncrona y bloqueante
/// Correspondence → Postmaster → Connectors → proveedor real (plan §0/§14). Timeout 30s (fijado en
/// el <see cref="HttpClient"/>, ver <c>AddCorrespondenceInfrastructure</c>).
///
/// <para>
/// Deliberadamente SIN retry, a diferencia de <c>ConnectorsClient</c>: la idempotencia de este envío
/// ya vive del lado de Postmaster, keyed por <c>CorrespondenceDraftId</c>
/// (<c>SendCorrespondenceMessageHandler.BuildIdempotencyKey</c>) — reintentar el mismo draft nunca
/// duplica el envío, así que un reintento manual del usuario desde la UI es completamente seguro.
/// Un retry automático acá no gana nada que esa idempotencia no cubra ya, y sí agrega: latencia
/// extra sobre una request que el usuario ya está esperando en vivo, y el riesgo de mantener la
/// conexión abierta más tiempo del necesario contra una falla que probablemente no era transitoria
/// (Postmaster ya intentó Connectors y CloudStorage puertas adentro antes de devolver error).
/// </para>
///
/// <para>
/// Los errores estructurados que devuelve Postmaster (<c>SendCorrespondenceMessageHandler.*</c>) se
/// propagan TAL CUAL — mismo <see cref="Error.Code"/>/<see cref="Error.Message"/>, sin traducir a un
/// código propio — porque <c>ErrorHttpMapping</c> (BuildingBlocks.Web, compartido entre servicios)
/// ya sabe mapear esos códigos al status HTTP correcto (403/409/502). Solo las fallas propias de
/// este cliente (token M2M no disponible, excepción de red, respuesta sin cuerpo interpretable, HTTP
/// inesperado sin cuerpo de error) usan códigos <c>PostmasterClient.*</c> nuevos — mismo patrón que
/// ya siguen <c>ConnectorsClient</c>/<c>CloudStorageClient</c> para el mismo problema de "relayar el
/// error de un servicio río abajo".
/// </para>
/// </summary>
internal sealed class PostmasterClient(
    HttpClient httpClient,
    ICorrespondenceServiceTokenAcquirer tokenAcquirer,
    ILogger<PostmasterClient> logger
) : IPostmasterClient
{
    /// <summary>Error.Code que Postmaster propaga tal cual cuando TODOS los destinatarios están suprimidos (ver <c>SendCorrespondenceMessageHandler.ApplySuppressionAsync</c>) — el único caso de falla que este cliente etiqueta distinto de "failed" en <see cref="CorrespondenceMetrics.DraftSendTotal"/> (Fase 16, plan §29).</summary>
    private const string AllRecipientsSuppressedErrorCode = "SendCorrespondenceMessageHandler.AllRecipientsSuppressed";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<Result<SendDraftPostmasterResult>> SendAsync(
        Guid tenantId,
        Guid draftId,
        Guid accountId,
        string subject,
        string html,
        string? text,
        IReadOnlyList<string> to,
        IReadOnlyList<string> cc,
        IReadOnlyList<string> bcc,
        IReadOnlyList<DraftAttachmentRef> attachments,
        ReplyContext? replyContext,
        CancellationToken ct = default
    )
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await SendCoreAsync(
            tenantId,
            draftId,
            accountId,
            subject,
            html,
            text,
            to,
            cc,
            bcc,
            attachments,
            replyContext,
            ct
        );
        RecordMetrics(tenantId, result, stopwatch.Elapsed);
        return result;
    }

    private async Task<Result<SendDraftPostmasterResult>> SendCoreAsync(
        Guid tenantId,
        Guid draftId,
        Guid accountId,
        string subject,
        string html,
        string? text,
        IReadOnlyList<string> to,
        IReadOnlyList<string> cc,
        IReadOnlyList<string> bcc,
        IReadOnlyList<DraftAttachmentRef> attachments,
        ReplyContext? replyContext,
        CancellationToken ct
    )
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        if (string.IsNullOrEmpty(token))
            return Result.Failure<SendDraftPostmasterResult>(
                new Error(
                    "PostmasterClient.ServiceAuthUnavailable",
                    "Could not acquire a service token to call Postmaster."
                )
            );

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "postmaster/correspondence-messages")
            {
                Content = JsonContent.Create(
                    BuildRequestBody(
                        tenantId,
                        draftId,
                        accountId,
                        subject,
                        html,
                        text,
                        to,
                        cc,
                        bcc,
                        attachments,
                        replyContext
                    ),
                    options: Json
                ),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClient.SendAsync(request, ct);
            return response.IsSuccessStatusCode
                ? await ReadSuccessAsync(response, ct)
                : await ReadFailureAsync(response, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Postmaster send call threw for draft {DraftId}.", draftId);
            return Result.Failure<SendDraftPostmasterResult>(new Error("PostmasterClient.RequestFailed", ex.Message));
        }
    }

    /// <summary>Fase 16, plan §29 — <see cref="CorrespondenceMetrics.DraftSendDuration"/>/<see cref="CorrespondenceMetrics.DraftSendTotal"/>, tags "tenant"/"status". Se registra acá (Infrastructure) y no en <c>SendDraftHandler</c> (Application) porque Application no puede depender de Infrastructure (ver CorrespondenceArchitectureTests).</summary>
    private static void RecordMetrics(Guid tenantId, Result<SendDraftPostmasterResult> result, TimeSpan elapsed)
    {
        var tenantTag = new KeyValuePair<string, object?>("tenant", tenantId.ToString());
        CorrespondenceMetrics.DraftSendDuration.Record(elapsed.TotalSeconds, tenantTag);

        var status =
            result.IsSuccess ? "sent"
            : result.Error.Code == AllRecipientsSuppressedErrorCode ? "suppressed"
            : "failed";
        CorrespondenceMetrics.DraftSendTotal.Add(1, tenantTag, new KeyValuePair<string, object?>("status", status));
    }

    /// <summary>Byte-a-byte compatible con <c>SendCorrespondenceMessageRequest</c> (Postmaster.Api.Requests) — mismos nombres de campo en camelCase.</summary>
    private static object BuildRequestBody(
        Guid tenantId,
        Guid draftId,
        Guid accountId,
        string subject,
        string html,
        string? text,
        IReadOnlyList<string> to,
        IReadOnlyList<string> cc,
        IReadOnlyList<string> bcc,
        IReadOnlyList<DraftAttachmentRef> attachments,
        ReplyContext? replyContext
    ) =>
        new
        {
            tenantId,
            correspondenceDraftId = draftId,
            accountId,
            subject,
            html,
            text,
            to,
            cc,
            bcc,
            attachments = attachments
                .Select(a => new
                {
                    fileId = a.FileId,
                    filename = a.Filename,
                    contentType = a.ContentType,
                    sizeBytes = a.SizeBytes,
                })
                .ToList(),
            replyContext = replyContext is null
                ? null
                : new
                {
                    inReplyToInternetMessageId = replyContext.InReplyToInternetMessageId,
                    references = replyContext.References,
                    replyToProviderMessageId = replyContext.ReplyToProviderMessageId,
                },
        };

    private static async Task<Result<SendDraftPostmasterResult>> ReadSuccessAsync(
        HttpResponseMessage response,
        CancellationToken ct
    )
    {
        SendResultDto? body;
        try
        {
            body = await response.Content.ReadFromJsonAsync<SendResultDto>(Json, ct);
        }
        catch (JsonException)
        {
            body = null;
        }

        return body is null
            ? Result.Failure<SendDraftPostmasterResult>(
                new Error("PostmasterClient.EmptyResponse", "Postmaster returned an unreadable body response.")
            )
            : Result.Success(new SendDraftPostmasterResult(body.SentMessageId, body.ProviderMessageId));
    }

    private static async Task<Result<SendDraftPostmasterResult>> ReadFailureAsync(
        HttpResponseMessage response,
        CancellationToken ct
    )
    {
        Error? error;
        try
        {
            error = await response.Content.ReadFromJsonAsync<Error>(Json, ct);
        }
        catch (JsonException)
        {
            error = null;
        }

        return Result.Failure<SendDraftPostmasterResult>(
            error
                ?? new Error(
                    "PostmasterClient.UnexpectedStatus",
                    $"Postmaster returned HTTP {(int)response.StatusCode}."
                )
        );
    }

    private sealed record SendResultDto(Guid SentMessageId, string? ProviderMessageId);
}
