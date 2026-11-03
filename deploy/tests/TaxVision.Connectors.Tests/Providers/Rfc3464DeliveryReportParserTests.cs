using System.Text;
using TaxVision.Connectors.Infrastructure.Providers.DeliveryReports;

namespace TaxVision.Connectors.Tests.Providers;

/// <summary>
/// Muestras con la forma real de cada servidor: Gmail adjunta solo los headers del original
/// (text/rfc822-headers), Exchange adjunta el correo entero (message/rfc822) y Postfix informa varios
/// destinatarios en el mismo aviso.
/// <para>
/// Fixtures (Providers/DeliveryReports/Fixtures): todos sintéticos, armados a mano con la estructura
/// pública de cada servidor; ninguno es un aviso real capturado. Gmail: muestras públicas de
/// mailer-daemon (multipart/report con multipart/related y aviso viejo en texto plano). Exchange Online:
/// formato de NDR documentado por Microsoft ("Remote Server returned …", "Original message headers").
/// Exchange en texto y formato viejo "#5.1.1 … ##rfc822;": variantes sin delivery-status. Postfix:
/// plantilla por defecto de bounce(5). Exim: plantilla por defecto ("The following address(es) failed").
/// </para>
/// </summary>
public class Rfc3464DeliveryReportParserTests
{
    private static readonly Rfc3464DeliveryReportParser Parser = new();

    private const string GmailBounce = """
        From: Mail Delivery Subsystem <mailer-daemon@googlemail.com>
        To: office@gmail.com
        Subject: Delivery Status Notification (Failure)
        Message-ID: <report-1@mx.google.com>
        MIME-Version: 1.0
        Content-Type: multipart/report; report-type=delivery-status; boundary="b1"

        --b1
        Content-Type: text/plain; charset=UTF-8

        Address not found
        --b1
        Content-Type: message/delivery-status

        Reporting-MTA: dns; googlemail.com
        Arrival-Date: Tue, 21 Oct 2026 12:00:00 -0700

        Final-Recipient: rfc822; nobody@example.com
        Action: failed
        Status: 5.1.1
        Remote-MTA: dns; mx.example.com
        Diagnostic-Code: smtp; 550 5.1.1 The email account that you tried to reach does not exist.

        --b1
        Content-Type: text/rfc822-headers

        From: Office <office@gmail.com>
        To: nobody@example.com
        Subject: Your documents
        Message-ID: <pm.0f8fad5bd9cb469fa16570867728950e@gmail.com>

        --b1--
        """;

    private const string ExchangeNdr = """
        From: postmaster@contoso.onmicrosoft.com
        To: office@contoso.com
        Subject: Undeliverable: Your documents
        MIME-Version: 1.0
        Content-Type: multipart/report; report-type=delivery-status; boundary="b2"

        --b2
        Content-Type: text/plain

        Delivery has failed to these recipients or groups.
        --b2
        Content-Type: message/delivery-status

        Reporting-MTA: dns;AM6P.eurprd.prod.outlook.com

        Original-Recipient: rfc822;full@example.org
        Final-Recipient: rfc822;full@example.org
        Action: failed
        Status: 5.2.2
        Diagnostic-Code: smtp;552 5.2.2 Mailbox full

        --b2
        Content-Type: message/rfc822

        From: Office <office@contoso.com>
        To: full@example.org
        Subject: Your documents
        Message-ID: <AM6P@outlook.com>
        Content-Type: text/plain

        Hello
        --b2--
        """;

    private const string PostfixTwoRecipients = """
        From: MAILER-DAEMON@mail.office.com (Mail Delivery System)
        To: office@office.com
        Subject: Undelivered Mail Returned to Sender
        MIME-Version: 1.0
        Content-Type: multipart/report; report-type=delivery-status; boundary="b3"

        --b3
        Content-Type: text/plain

        I'm sorry to have to inform you that your message could not be delivered.
        --b3
        Content-Type: message/delivery-status

        Reporting-MTA: dns; mail.office.com

        Final-Recipient: rfc822; gone@example.com
        Action: failed
        Status: 5.1.1

        Final-Recipient: rfc822; slow@example.com
        Action: delayed
        Status: 4.4.1
        Diagnostic-Code: X-Postfix; connect to mx.example.com timed out

        --b3
        Content-Type: text/rfc822-headers

        Message-ID: <pm.11111111111111111111111111111111@office.com>
        Subject: Reminder

        --b3--
        """;

    private static byte[] Bytes(string mime) =>
        Encoding.UTF8.GetBytes(mime.Replace("\r\n", "\n").Replace("\n", "\r\n"));

    [Fact]
    public void Parse_reads_a_Gmail_bounce_with_only_the_original_headers()
    {
        var entry = Assert.Single(Parser.Parse(Bytes(GmailBounce)).Entries);

        Assert.Equal("pm.0f8fad5bd9cb469fa16570867728950e@gmail.com", entry.OriginalMessageId);
        Assert.Equal("nobody@example.com", entry.Recipient);
        Assert.Equal("failed", entry.Action);
        Assert.Equal("5.1.1", entry.StatusCode);
        Assert.StartsWith("550 5.1.1", entry.Diagnostic);
    }

    [Fact]
    public void Parse_reads_an_Exchange_NDR_with_the_whole_original_attached()
    {
        var entry = Assert.Single(Parser.Parse(Bytes(ExchangeNdr)).Entries);

        Assert.Equal("AM6P@outlook.com", entry.OriginalMessageId);
        Assert.Equal("full@example.org", entry.Recipient);
        Assert.Equal("5.2.2", entry.StatusCode);
    }

    [Fact]
    public void Parse_returns_one_entry_per_recipient()
    {
        var entries = Parser.Parse(Bytes(PostfixTwoRecipients)).Entries;

        Assert.Equal(2, entries.Count);
        Assert.Equal(
            ("gone@example.com", "failed", "5.1.1"),
            (entries[0].Recipient, entries[0].Action, entries[0].StatusCode)
        );
        Assert.Equal(
            ("slow@example.com", "delayed", "4.4.1"),
            (entries[1].Recipient, entries[1].Action, entries[1].StatusCode)
        );
        Assert.All(entries, e => Assert.Equal("pm.11111111111111111111111111111111@office.com", e.OriginalMessageId));
    }

    [Fact]
    public void Parse_returns_nothing_for_a_normal_email()
    {
        const string normal = """
            From: maria@client.com
            To: office@gmail.com
            Subject: Undeliverable? No, just a question
            Content-Type: text/plain

            Hi!
            """;

        Assert.Empty(Parser.Parse(Bytes(normal)).Entries);
    }

    [Fact]
    public void Parse_returns_nothing_for_garbage()
    {
        Assert.Empty(Parser.Parse([0x00, 0x01, 0x02]).Entries);
    }

    private static byte[] Fixture(string name)
    {
        using var stream = typeof(Rfc3464DeliveryReportParserTests).Assembly.GetManifestResourceStream(
            $"DeliveryReports.{name}"
        )!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    [Fact]
    public void Parse_reads_a_Gmail_report_and_its_human_summary()
    {
        var report = Parser.Parse(Fixture("gmail-rfc3464-address-not-found.eml"));

        var entry = Assert.Single(report.Entries);
        Assert.Equal("pm.0f8fad5bd9cb469fa16570867728950e@gmail.com", entry.OriginalMessageId);
        Assert.Equal(("nobody@example.com", "failed", "5.1.1"), (entry.Recipient, entry.Action, entry.StatusCode));
        Assert.Equal("Delivery Status Notification (Failure)", report.Subject);
        Assert.Equal("mailer-daemon@googlemail.com", report.From);
        Assert.StartsWith("** Address not found **", report.Summary);
        Assert.Contains("wasn't delivered to nobody@example.com", report.Summary);
        Assert.DoesNotContain("CONFIDENTIAL-ORIGINAL", report.Summary);
    }

    [Fact]
    public void Parse_reads_an_Exchange_Online_NDR_and_keeps_the_original_headers_out_of_the_summary()
    {
        var report = Parser.Parse(Fixture("exchange-online-ndr-report.eml"));

        var entry = Assert.Single(report.Entries);
        Assert.Equal(
            "AM6PR01MB1234ABCDEF0123456789@AM6PR01MB1234.eurprd01.prod.exchangelabs.com",
            entry.OriginalMessageId
        );
        Assert.Equal(("nobody@example.com", "failed", "5.1.10"), (entry.Recipient, entry.Action, entry.StatusCode));
        Assert.StartsWith("550 5.1.10 RESOLVER.ADR.RecipientNotFound", entry.Diagnostic);
        Assert.Equal("Undeliverable: Your 2025 tax documents", report.Subject);
        Assert.Equal("postmaster@contoso.onmicrosoft.com", report.From);
        Assert.StartsWith("Your message to nobody@example.com couldn't be delivered.", report.Summary);
        // La sección para administradores y los headers del original no son para el usuario.
        Assert.DoesNotContain("Generating server", report.Summary);
        Assert.DoesNotContain("CONFIDENTIAL-ORIGINAL", report.Summary);
    }

    [Fact]
    public void Parse_falls_back_to_the_text_of_an_Exchange_NDR_without_delivery_status()
    {
        var report = Parser.Parse(Fixture("exchange-text-only-ndr.eml"));

        var entry = Assert.Single(report.Entries);
        // El id sale del correo adjunto, nunca de los headers copiados en el texto.
        Assert.Equal("pm.22222222222222222222222222222222@contoso.com", entry.OriginalMessageId);
        Assert.Equal("maria@client-example.com", entry.Recipient);
        Assert.Equal("failed", entry.Action);
        Assert.Equal("5.1.1", entry.StatusCode);
        Assert.Equal("550 5.1.1 RESOLVER.ADR.RecipNotFound; not found", entry.Diagnostic);
        Assert.StartsWith("Delivery has failed to these recipients or groups:", report.Summary);
        Assert.DoesNotContain("CONFIDENTIAL-ORIGINAL", report.Summary);
        Assert.DoesNotContain("decoy", report.Summary);
    }

    [Fact]
    public void Parse_falls_back_to_the_legacy_Exchange_line_and_the_notice_In_Reply_To()
    {
        var entry = Assert.Single(Parser.Parse(Fixture("exchange-legacy-text-ndr.eml")).Entries);

        Assert.Equal("pm.33333333333333333333333333333333@contoso.com", entry.OriginalMessageId);
        Assert.Equal(("gone@example.net", "failed", "5.1.1"), (entry.Recipient, entry.Action, entry.StatusCode));
        Assert.Contains("User unknown", entry.Diagnostic);
    }

    [Fact]
    public void Parse_reads_a_Postfix_report_through_delivery_status()
    {
        var report = Parser.Parse(Fixture("postfix-bounce.eml"));

        var entry = Assert.Single(report.Entries);
        Assert.Equal("pm.11111111111111111111111111111111@office-example.com", entry.OriginalMessageId);
        Assert.Equal(("gone@example.com", "failed", "5.1.1"), (entry.Recipient, entry.Action, entry.StatusCode));
        Assert.Equal("MAILER-DAEMON@mail.office-example.com", report.From);
        Assert.StartsWith("This is the mail system at host mail.office-example.com.", report.Summary);
    }

    [Fact]
    public void Parse_falls_back_to_Postfix_text_lines_one_entry_per_recipient()
    {
        var entries = Parser.Parse(Fixture("postfix-text-only-bounce.eml")).Entries;

        Assert.Equal(2, entries.Count);
        Assert.All(
            entries,
            e => Assert.Equal("pm.44444444444444444444444444444444@office-example.com", e.OriginalMessageId)
        );
        Assert.Equal(
            ("first@example.com", "failed", "5.1.1"),
            (entries[0].Recipient, entries[0].Action, entries[0].StatusCode)
        );
        Assert.Equal(
            ("second@example.org", "failed", "5.2.2"),
            (entries[1].Recipient, entries[1].Action, entries[1].StatusCode)
        );
        Assert.Contains("Mailbox full", entries[1].Diagnostic);
    }

    [Fact]
    public void Parse_falls_back_to_a_plain_Gmail_mailbox_full_notice_and_ignores_the_quoted_original()
    {
        var report = Parser.Parse(Fixture("gmail-plain-mailbox-full.eml"));

        // Solo full@example.org: la línea "<decoy@…>: … 550" está debajo de "Original message".
        var entry = Assert.Single(report.Entries);
        Assert.Equal("pm.55555555555555555555555555555555@gmail.com", entry.OriginalMessageId);
        Assert.Equal(("full@example.org", "failed", "5.2.2"), (entry.Recipient, entry.Action, entry.StatusCode));
        Assert.StartsWith("552 5.2.2 The email account", entry.Diagnostic);
        Assert.DoesNotContain("CONFIDENTIAL-ORIGINAL", report.Summary);
    }

    [Fact]
    public void Parse_reads_a_mailbox_full_report_with_a_temporary_code()
    {
        var entry = Assert.Single(Parser.Parse(Fixture("mailbox-full-422-report.eml")).Entries);

        Assert.Equal("pm.66666666666666666666666666666666@office-example.com", entry.OriginalMessageId);
        Assert.Equal(("busy@example.net", "failed", "4.2.2"), (entry.Recipient, entry.Action, entry.StatusCode));
        Assert.Equal("452 4.2.2 Mailbox full", entry.Diagnostic);
    }

    [Fact]
    public void Parse_reads_a_delayed_Exchange_report()
    {
        var report = Parser.Parse(Fixture("exchange-delayed-report.eml"));

        var entry = Assert.Single(report.Entries);
        Assert.Equal(("slow@example.com", "delayed", "4.4.7"), (entry.Recipient, entry.Action, entry.StatusCode));
        Assert.Equal("Delivery delayed: Signed engagement letter", report.Subject);
    }

    [Fact]
    public void Parse_falls_back_to_a_plain_Gmail_delay_notice()
    {
        var entry = Assert.Single(Parser.Parse(Fixture("gmail-plain-delayed.eml")).Entries);

        Assert.Equal("pm.77777777777777777777777777777777@gmail.com", entry.OriginalMessageId);
        Assert.Equal(("slow@example.com", "delayed", "4.3.0"), (entry.Recipient, entry.Action, entry.StatusCode));
        Assert.StartsWith("451 4.3.0", entry.Diagnostic);
    }

    [Fact]
    public void Parse_fallback_reads_a_Spanish_Exchange_notice_answered_through_References()
    {
        const string spanish = """
            From: postmaster@contoso.com
            To: office@contoso.com
            Subject: No se puede entregar: Factura
            References: <hilo-1@example.com> <pm.88888888888888888888888888888888@contoso.com>
            Content-Type: text/plain; charset=UTF-8

            Su mensaje para cliente@example.com no se pudo entregar.

            Información de diagnóstico para administradores:

            cliente@example.com
            Remote Server returned '550 5.1.1 User unknown'
            """;

        var report = Parser.Parse(Bytes(spanish));

        var entry = Assert.Single(report.Entries);
        Assert.Equal("pm.88888888888888888888888888888888@contoso.com", entry.OriginalMessageId);
        Assert.Equal(("cliente@example.com", "failed", "5.1.1"), (entry.Recipient, entry.Action, entry.StatusCode));
        Assert.Equal("Su mensaje para cliente@example.com no se pudo entregar.", report.Summary);
    }

    [Fact]
    public void Parse_fallback_ignores_a_person_who_pastes_a_bounce_into_a_reply()
    {
        // Responde a nuestro correo (In-Reply-To = pm.) y pega texto de rebote: no es un aviso de sistema.
        const string reply = """
            From: Maria <maria@client.com>
            To: office@gmail.com
            Subject: Re: Undeliverable: Your documents
            In-Reply-To: <pm.99999999999999999999999999999999@gmail.com>
            Content-Type: text/plain

            I got this when I forwarded it:
            <boss@client.com>: host mx.client.com said: 550 5.1.1 User unknown
            boss@client.com
            Remote Server returned '550 5.1.1 RESOLVER.ADR.RecipNotFound; not found'
            """;

        Assert.Empty(Parser.Parse(Bytes(reply)).Entries);
    }

    [Fact]
    public void Parse_fallback_needs_the_original_message_id()
    {
        // Aviso de sistema con destinatario claro pero sin adjunto ni In-Reply-To: no se adivina.
        const string orphan = """
            From: MAILER-DAEMON@mail.office.com
            To: office@office.com
            Subject: Undelivered Mail Returned to Sender
            Content-Type: text/plain

            <gone@example.com>: host mx.example.com said: 550 5.1.1 User unknown
            """;

        var report = Parser.Parse(Bytes(orphan));

        Assert.Empty(report.Entries);
        Assert.Equal("Undelivered Mail Returned to Sender", report.Subject);
    }

    [Fact]
    public void Parse_builds_the_summary_from_an_html_only_notice()
    {
        const string htmlOnly = """
            From: postmaster@contoso.com
            To: office@contoso.com
            Subject: Undeliverable: Hello
            In-Reply-To: <pm.12121212121212121212121212121212@contoso.com>
            Content-Type: text/html; charset=UTF-8

            <html><head><style>p{color:red}</style></head><body>
            <p>Your message to <b>x@example.com</b> couldn&#39;t be delivered.</p>
            <p>Original message headers:</p><pre>Subject: CONFIDENTIAL-ORIGINAL-SUBJECT</pre>
            </body></html>
            """;

        var report = Parser.Parse(Bytes(htmlOnly));

        Assert.Equal("Your message to x@example.com couldn't be delivered.", report.Summary);
        var entry = Assert.Single(report.Entries);
        Assert.Equal("x@example.com", entry.Recipient);
        Assert.Null(entry.StatusCode);
    }
}
