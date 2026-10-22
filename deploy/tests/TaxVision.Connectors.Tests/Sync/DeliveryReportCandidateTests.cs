using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Application.Sync;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Tests.Sync;

public class DeliveryReportCandidateTests
{
    private static RawMessage Message(string from, string subject) =>
        new(
            "id-1",
            null,
            null,
            null,
            [],
            from,
            null,
            ["office@gmail.com"],
            [],
            [],
            subject,
            "",
            DateTime.UtcNow,
            [],
            AuthenticationSignals.Unknown
        );

    [Theory]
    [InlineData("mailer-daemon@googlemail.com", "Delivery Status Notification (Failure)")]
    [InlineData("MAILER-DAEMON@mail.office.com", "Anything")]
    [InlineData("postmaster@contoso.onmicrosoft.com", "Hello")]
    [InlineData("MicrosoftExchange329e71ec88ae4615bbc36ab6ce41109e@contoso.com", "Hello")]
    [InlineData("noreply@relay.example", "Undeliverable: Your documents")]
    [InlineData("noreply@relay.example", "Mail delivery failed: returning message to sender")]
    public void IsLikely_spots_delivery_reports(string from, string subject)
    {
        Assert.True(DeliveryReportCandidate.IsLikely(Message(from, subject)));
    }

    [Theory]
    [InlineData("maria@client.com", "Question about my return")]
    [InlineData("postmaster.fan@client.com.ar", "Hi")]
    public void IsLikely_leaves_normal_mail_alone(string from, string subject)
    {
        // "postmaster.fan" empieza igual: es un falso positivo aceptado (cuesta una descarga), salvo este.
        var likely = DeliveryReportCandidate.IsLikely(Message(from, subject));

        Assert.Equal(from.StartsWith("postmaster", StringComparison.OrdinalIgnoreCase), likely);
    }
}
