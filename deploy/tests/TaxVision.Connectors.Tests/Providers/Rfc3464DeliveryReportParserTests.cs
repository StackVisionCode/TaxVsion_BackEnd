using System.Text;
using TaxVision.Connectors.Infrastructure.Providers.DeliveryReports;

namespace TaxVision.Connectors.Tests.Providers;

/// <summary>
/// Muestras con la forma real de cada servidor: Gmail adjunta solo los headers del original
/// (text/rfc822-headers), Exchange adjunta el correo entero (message/rfc822) y Postfix informa varios
/// destinatarios en el mismo aviso.
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
        var entry = Assert.Single(Parser.Parse(Bytes(GmailBounce)));

        Assert.Equal("pm.0f8fad5bd9cb469fa16570867728950e@gmail.com", entry.OriginalMessageId);
        Assert.Equal("nobody@example.com", entry.Recipient);
        Assert.Equal("failed", entry.Action);
        Assert.Equal("5.1.1", entry.StatusCode);
        Assert.StartsWith("550 5.1.1", entry.Diagnostic);
    }

    [Fact]
    public void Parse_reads_an_Exchange_NDR_with_the_whole_original_attached()
    {
        var entry = Assert.Single(Parser.Parse(Bytes(ExchangeNdr)));

        Assert.Equal("AM6P@outlook.com", entry.OriginalMessageId);
        Assert.Equal("full@example.org", entry.Recipient);
        Assert.Equal("5.2.2", entry.StatusCode);
    }

    [Fact]
    public void Parse_returns_one_entry_per_recipient()
    {
        var entries = Parser.Parse(Bytes(PostfixTwoRecipients));

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

        Assert.Empty(Parser.Parse(Bytes(normal)));
    }

    [Fact]
    public void Parse_returns_nothing_for_garbage()
    {
        Assert.Empty(Parser.Parse([0x00, 0x01, 0x02]));
    }
}
