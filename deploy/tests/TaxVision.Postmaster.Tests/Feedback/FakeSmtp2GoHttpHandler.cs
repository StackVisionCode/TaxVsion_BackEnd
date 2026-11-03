using System.Net;
using System.Text;

namespace TaxVision.Postmaster.Tests.Feedback;

/// <summary>API de SMTP2GO falsa: contesta en orden lo encolado y anota cada pedido con su cuerpo.</summary>
internal sealed class FakeSmtp2GoHttpHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();

    public List<HttpRequestMessage> Requests { get; } = [];
    public List<string> Bodies { get; } = [];

    public void Enqueue(HttpStatusCode status, string json) =>
        _responses.Enqueue(
            new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") }
        );

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct));
        return _responses.Count > 0
            ? _responses.Dequeue()
            : new HttpResponseMessage(HttpStatusCode.InternalServerError);
    }

    /// <summary>Respuesta de <c>activity/search</c>: eventos y, si hay más páginas, su <c>continue_token</c>.</summary>
    public static string Page(string? continueToken, params string[] events)
    {
        var token = continueToken is null ? "null" : $"\"{continueToken}\"";
        return $$$"""{"request_id":"r1","data":{"total_events":{{{events.Length}}},"continue_token":{{{token}}},"events":[{{{string.Join(",", events)}}}]}}""";
    }

    public static string ActivityEvent(
        string emailId,
        string eventName,
        string recipient = "customer@example.com",
        string date = "2026-10-21T06:01:00Z"
    ) =>
        $$"""{"email_id":"{{emailId}}","event":"{{eventName}}","recipient":"{{recipient}}","date":"{{date}}","smtp_response":"250 OK"}""";
}
