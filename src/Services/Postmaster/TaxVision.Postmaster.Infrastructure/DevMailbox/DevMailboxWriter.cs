using System.Net;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Infrastructure.DevMailbox;

// Escribe una copia .html de cada correo saliente; nunca lanza (el envío real no depende de esto).
public sealed class DevMailboxWriter
{
    public const int MaxFiles = 200;
    private const int MaxSegmentLength = 60;

    private readonly ILogger<DevMailboxWriter> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();

    public DevMailboxWriter(
        DevMailboxOptions options,
        IHostEnvironment? environment,
        ILogger<DevMailboxWriter> logger,
        TimeProvider timeProvider
    )
    {
        _logger = logger;
        _timeProvider = timeProvider;
        if (!options.Enabled)
            return;

        // Candado: fuera de Development se ignora aunque Enabled=true.
        if (environment is null || !environment.IsDevelopment())
        {
            logger.LogWarning(
                "Postmaster:DevMailbox:Enabled=true ignored: environment is {Environment}, only Development is allowed.",
                environment?.EnvironmentName ?? "(unknown)"
            );
            return;
        }

        Directory = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Directory));
        logger.LogWarning("Dev mailbox active: outgoing emails are also written to {Directory}.", Directory);
    }

    // Ruta absoluta del buzón; null = apagado.
    public string? Directory { get; }

    public bool IsActive => Directory is not null;

    public void Write(SentMessage message, RenderedContent content, string fromAddress, string? fromDisplayName)
    {
        if (Directory is null)
            return;

        try
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var firstTo =
                message.Recipients.FirstOrDefault(r => r.Type == RecipientType.To)?.Address
                ?? message.Recipients.FirstOrDefault()?.Address
                ?? "no-recipient";
            var html = BuildDocument(message, content, fromAddress, fromDisplayName, now);

            lock (_gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                var path = Path.Combine(Directory, BuildFileName(now, firstTo, content.Subject));
                // Choque de nombre (mismo ms): sufijo corto para no pisar la copia anterior.
                if (File.Exists(path))
                    path = Path.ChangeExtension(path, null) + $"_{Guid.NewGuid().ToString("N")[..8]}.html";
                File.WriteAllText(path, html, new UTF8Encoding(false));
                ApplyRetention(Directory);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dev mailbox could not write SentMessage {SentMessageId}.", message.Id);
        }
    }

    // yyyyMMdd-HHmmss-fff_<destinatario>_<asunto>.html
    public static string BuildFileName(DateTime utc, string recipient, string subject) =>
        $"{utc:yyyyMMdd-HHmmss-fff}_{Sanitize(recipient, "no-recipient")}_{Sanitize(subject, "no-subject")}.html";

    // Deja letras, dígitos, '@', '.', '-', '_'; el resto pasa a '-' (colapsado) y se recorta.
    public static string Sanitize(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var sb = new StringBuilder(value.Length);
        foreach (var c in value.Trim())
        {
            var keep = char.IsAsciiLetterOrDigit(c) || c is '@' or '.' or '-' or '_';
            if (keep)
                sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-')
                sb.Append('-');
        }

        var result = sb.ToString();
        if (result.Length > MaxSegmentLength)
            result = result[..MaxSegmentLength];
        result = result.Trim('-', '.', '_');
        return result.Length == 0 ? fallback : result;
    }

    // Conserva los MaxFiles más nuevos (el nombre empieza por la marca de tiempo UTC).
    private void ApplyRetention(string directory)
    {
        var stale = new DirectoryInfo(directory)
            .GetFiles("*.html")
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .Skip(MaxFiles);
        foreach (var file in stale)
        {
            try
            {
                file.Delete();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Dev mailbox could not delete {File}.", file.FullName);
            }
        }
    }

    private static string BuildDocument(
        SentMessage message,
        RenderedContent content,
        string fromAddress,
        string? fromDisplayName,
        DateTime nowUtc
    )
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
        string Join(RecipientType type) =>
            string.Join(", ", message.Recipients.Where(r => r.Type == type).Select(r => r.Address));

        var from = string.IsNullOrWhiteSpace(fromDisplayName) ? fromAddress : $"{fromDisplayName} <{fromAddress}>";
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html>");
        sb.AppendLine($"<html><head><meta charset=\"utf-8\"><title>{E(content.Subject)}</title></head><body>");
        sb.AppendLine(
            "<div id=\"dev-mailbox-header\" style=\"font:13px/1.5 monospace;background:#fff3cd;color:#222;"
                + "border:1px solid #e0c36a;padding:8px 12px;margin:0 0 16px\">"
        );
        sb.AppendLine("<strong>DEV MAILBOX</strong> (local copy; the real send also happened)<br>");
        sb.AppendLine($"To: {E(Join(RecipientType.To))}<br>");
        var cc = Join(RecipientType.Cc);
        if (cc.Length > 0)
            sb.AppendLine($"Cc: {E(cc)}<br>");
        var bcc = Join(RecipientType.Bcc);
        if (bcc.Length > 0)
            sb.AppendLine($"Bcc: {E(bcc)}<br>");
        sb.AppendLine($"From: {E(from)}<br>");
        sb.AppendLine($"Subject: {E(content.Subject)}<br>");
        sb.AppendLine($"Timestamp (UTC): {nowUtc:yyyy-MM-dd HH:mm:ss.fff}<br>");
        if (!string.IsNullOrWhiteSpace(message.TemplateKey))
            sb.AppendLine($"Template: {E(message.TemplateKey)}<br>");
        sb.AppendLine($"Provider: {E(message.ProviderCode)} | SentMessageId: {message.Id}");
        sb.AppendLine("</div>");
        sb.AppendLine(content.Html);
        if (!string.IsNullOrWhiteSpace(content.Text))
        {
            sb.AppendLine("<hr><h3 style=\"font-family:monospace\">Text body</h3>");
            sb.AppendLine($"<pre id=\"dev-mailbox-text\" style=\"white-space:pre-wrap\">{E(content.Text)}</pre>");
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }
}
