using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Email.Sending.Campaign;

namespace TaxVision.Notification.Infrastructure.Email.Campaign;

/// <summary>
/// Adapter de <see cref="ICampaignEmailProvider"/> por la <b>API HTTP de SMTP2GO</b>
/// (<c>POST {BaseUrl}email/send</c>, auth header <c>X-Smtp2go-Api-Key</c>). Recomendado para volumen:
/// stateless, sin handshake SMTP por correo, concurrencia trivial. La correlación con el webhook se
/// mantiene fijando el <c>Message-Id</c> vía <c>custom_headers</c> (el mismo que el adapter SMTP), que
/// SMTP2GO devuelve en el webhook. Respeta el <see cref="ICampaignEmailThrottle"/> antes de cada envío.
/// </summary>
public sealed class Smtp2GoApiCampaignEmailProvider(
    HttpClient http,
    IOptions<CampaignEmailOptions> options,
    ICampaignEmailThrottle throttle,
    ILogger<Smtp2GoApiCampaignEmailProvider> logger
) : ICampaignEmailProvider
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public string Code => options.Value.Provider;

    public async Task<CampaignEmailSendResult> SendAsync(CampaignEmailMessage message, CancellationToken ct = default)
    {
        var api = options.Value.Api;
        if (string.IsNullOrWhiteSpace(api.ApiKey) || string.IsNullOrWhiteSpace(api.FromAddress))
        {
            logger.LogError("CampaignEmail SMTP2GO API no configurada (ApiKey/FromAddress).");
            return CampaignEmailSendResult.Fail("email.providerNotConfigured");
        }

        await throttle.WaitTurnAsync(ct);

        var messageId = CampaignEmailCorrelation.BuildMessageId(
            message.TenantId,
            message.CampaignId,
            message.RunId,
            message.DispatchId
        );

        var payload = new SendRequest
        {
            Sender = Format(api.FromName, api.FromAddress!),
            To = [Format(message.ToName, message.To)],
            Subject = string.IsNullOrWhiteSpace(message.Subject) ? "(no subject)" : message.Subject,
            HtmlBody = message.HtmlBody,
            TextBody = string.IsNullOrWhiteSpace(message.TextBody) ? null : message.TextBody,
            CustomHeaders = [new CustomHeader { Header = "Message-Id", Value = $"<{messageId}>" }],
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "email/send")
            {
                Content = JsonContent.Create(payload, options: JsonOpts),
            };
            request.Headers.Add("X-Smtp2go-Api-Key", api.ApiKey);

            using var response = await http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("SMTP2GO API {Status} para {To}: {Body}", (int)response.StatusCode, message.To, Trunc(body));
                return CampaignEmailSendResult.Fail("email.providerUnavailable");
            }

            // { "data": { "succeeded": 1, "failed": 0, "email_id": "...", "failures": [...] } }
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("data", out var data)
                && data.TryGetProperty("succeeded", out var ok)
                && ok.TryGetInt32(out var succeeded)
                && succeeded >= 1)
            {
                return CampaignEmailSendResult.Ok(messageId);
            }

            logger.LogWarning("SMTP2GO API no aceptó {To}: {Body}", message.To, Trunc(body));
            return CampaignEmailSendResult.Fail("email.rejected");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "No se pudo alcanzar la API de SMTP2GO para {To}.", message.To);
            return CampaignEmailSendResult.Fail("email.providerUnavailable");
        }
    }

    private static string Format(string? name, string address) =>
        string.IsNullOrWhiteSpace(name) ? address : $"{name} <{address}>";

    private static string Trunc(string s) => s.Length <= 300 ? s : s[..300];

    private sealed class SendRequest
    {
        [JsonPropertyName("sender")]
        public string Sender { get; init; } = default!;

        [JsonPropertyName("to")]
        public string[] To { get; init; } = [];

        [JsonPropertyName("subject")]
        public string Subject { get; init; } = default!;

        [JsonPropertyName("html_body")]
        public string HtmlBody { get; init; } = default!;

        [JsonPropertyName("text_body")]
        public string? TextBody { get; init; }

        [JsonPropertyName("custom_headers")]
        public CustomHeader[] CustomHeaders { get; init; } = [];
    }

    private sealed class CustomHeader
    {
        [JsonPropertyName("header")]
        public string Header { get; init; } = default!;

        [JsonPropertyName("value")]
        public string Value { get; init; } = default!;
    }
}
