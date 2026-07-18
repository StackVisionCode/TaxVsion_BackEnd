using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Application.Providers;
using TaxVision.Postmaster.Application.Sending;
using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Infrastructure.Providers.Assets;

namespace TaxVision.Postmaster.Infrastructure.Providers.Connectors;

/// <summary>
/// Implementación de <see cref="IOAuthEmailSender"/> vía el M2M de Connectors
/// (<c>POST /connectors/accounts/{accountId}/send</c>, D3 §4.4) — reusa el mismo
/// <see cref="IPostmasterServiceTokenAcquirer"/> ya en producción para CloudStorage, mismo criterio de
/// "un solo adquirente de tokens M2M por servicio". A diferencia de <c>SmtpEmailSender</c> no hay
/// granularidad por destinatario individual: Connectors trata el envío como una sola llamada atómica,
/// así que <see cref="SendResult.RecipientOutcomes"/> queda todo-Sent o todo-Rejected.
/// </summary>
public sealed class ConnectorsSendClient(
    HttpClient httpClient,
    IPostmasterServiceTokenAcquirer tokenAcquirer,
    ILogger<ConnectorsSendClient> logger
) : IOAuthEmailSender
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<SendResult> SendAsync(
        SentMessage message,
        RenderedContent content,
        ResolvedOAuthProvider provider,
        string? inReplyToInternetMessageId,
        IReadOnlyList<string>? references,
        string? replyToProviderMessageId,
        IReadOnlyList<OutboundAttachmentBytes> attachments,
        CancellationToken ct
    )
    {
        var token = await tokenAcquirer.GetTokenAsync(message.TenantId, ct);
        if (string.IsNullOrEmpty(token))
        {
            logger.LogWarning("No service token available to call Connectors for tenant {TenantId}.", message.TenantId);
            return Failed(message, "No Connectors credentials available.");
        }

        var request = BuildRequest(
            message,
            content,
            inReplyToInternetMessageId,
            references,
            replyToProviderMessageId,
            attachments
        );
        try
        {
            using var response = await PostSendAsync(provider.AccountId, token, request, ct);
            return await ParseResponseAsync(response, message, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Connectors send call failed for SentMessage {SentMessageId}.", message.Id);
            return Failed(message, ex.Message);
        }
    }

    private static SendMessageRequestDto BuildRequest(
        SentMessage message,
        RenderedContent content,
        string? inReplyToInternetMessageId,
        IReadOnlyList<string>? references,
        string? replyToProviderMessageId,
        IReadOnlyList<OutboundAttachmentBytes> attachments
    ) =>
        new(
            message.TenantId,
            content.Subject,
            content.Html,
            content.Text,
            AddressesOf(message, RecipientType.To),
            AddressesOf(message, RecipientType.Cc),
            AddressesOf(message, RecipientType.Bcc),
            ReplyToDisplayAddress: null,
            inReplyToInternetMessageId,
            references,
            replyToProviderMessageId,
            attachments.Count == 0 ? null : attachments.Select(ToAttachmentDto).ToList()
        );

    private static SendMessageAttachmentRequestDto ToAttachmentDto(OutboundAttachmentBytes attachment) =>
        new(attachment.Filename, attachment.ContentType, Convert.ToBase64String(attachment.Content));

    private static IReadOnlyList<string> AddressesOf(SentMessage message, RecipientType type) =>
        message
            .Recipients.Where(r => r.Type == type && r.Status != RecipientStatus.Suppressed)
            .Select(r => r.Address)
            .ToList();

    private Task<HttpResponseMessage> PostSendAsync(
        Guid accountId,
        string token,
        SendMessageRequestDto request,
        CancellationToken ct
    )
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"connectors/accounts/{accountId}/send")
        {
            Content = JsonContent.Create(request, options: Json),
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return httpClient.SendAsync(httpRequest, ct);
    }

    private async Task<SendResult> ParseResponseAsync(
        HttpResponseMessage response,
        SentMessage message,
        CancellationToken ct
    )
    {
        if (response.IsSuccessStatusCode)
        {
            var payload = await response.Content.ReadFromJsonAsync<SendMessageResultDto>(Json, ct);
            return Succeeded(message, payload?.ProviderMessageId);
        }

        var error = await response.Content.ReadFromJsonAsync<ErrorDto>(Json, ct);
        var reason = error is null
            ? $"Connectors send failed ({(int)response.StatusCode})."
            : $"{error.Code}: {error.Message}";
        logger.LogWarning(
            "Connectors send rejected SentMessage {SentMessageId} ({Status}): {Reason}",
            message.Id,
            (int)response.StatusCode,
            reason
        );
        return Failed(message, reason);
    }

    private static SendResult Succeeded(SentMessage message, string? providerMessageId) =>
        new(
            true,
            providerMessageId,
            null,
            message
                .Recipients.Select(r => new RecipientSendOutcome(r.Id, r.Address, RecipientSendStatus.Sent, null))
                .ToList()
        );

    private static SendResult Failed(SentMessage message, string reason) =>
        new(
            false,
            null,
            reason,
            message
                .Recipients.Select(r => new RecipientSendOutcome(r.Id, r.Address, RecipientSendStatus.Rejected, reason))
                .ToList()
        );

    private sealed record SendMessageRequestDto(
        Guid TenantId,
        string Subject,
        string Html,
        string? Text,
        IReadOnlyList<string> To,
        IReadOnlyList<string> Cc,
        IReadOnlyList<string> Bcc,
        string? ReplyToDisplayAddress,
        string? InReplyToInternetMessageId,
        IReadOnlyList<string>? References,
        string? ReplyToProviderMessageId,
        IReadOnlyList<SendMessageAttachmentRequestDto>? Attachments
    );

    private sealed record SendMessageAttachmentRequestDto(string Filename, string ContentType, string ContentBase64);

    private sealed record SendMessageResultDto(string? ProviderMessageId, string? ProviderThreadId, DateTime SentAtUtc);

    private sealed record ErrorDto(string Code, string Message);
}
