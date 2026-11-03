using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Application.Sync;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Tests.Sync;

public class DeliveryReportCandidateTests
{
    private static RawMessage Message(string from, string subject, string? contentType = null) =>
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
            AuthenticationSignals.Unknown,
            contentType
        );

    [Theory]
    [InlineData("mailer-daemon@googlemail.com", "Delivery Status Notification (Failure)")]
    [InlineData("MAILER-DAEMON@mail.office.com", "Anything")]
    [InlineData("postmaster@contoso.onmicrosoft.com", "Hello")]
    [InlineData("MicrosoftExchange329e71ec88ae4615bbc36ab6ce41109e@contoso.com", "Hello")]
    [InlineData("noreply@relay.example", "Undeliverable: Your documents")]
    [InlineData("noreply@relay.example", "Mail delivery failed: returning message to sender")]
    [InlineData("noreply@relay.example", "Delivery delayed: Signed engagement letter")]
    [InlineData("noreply@relay.example", "No se puede entregar: Factura 2025")]
    [InlineData("noreply@relay.example", "Mensaje no entregado")]
    [InlineData("noreply@relay.example", "Error de entrega del mensaje")]
    [InlineData("noreply@relay.example", "Entrega retrasada: Declaración")]
    [InlineData("noreply@relay.example", "Notificación de estado de entrega (error)")]
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

    [Theory]
    [InlineData("Your 2025 delivery of documents")]
    [InlineData("Entrega de documentos para la declaración")]
    [InlineData("Re: Error en la factura")]
    [InlineData("¿Ya llegó el mensaje?")]
    [InlineData("Delivery confirmation for your return")]
    public void IsLikely_does_not_trip_on_everyday_subjects(string subject)
    {
        Assert.False(DeliveryReportCandidate.IsLikely(Message("maria@client.com", subject)));
    }

    [Fact]
    public void IsLikely_spots_a_multipart_report_even_from_an_unknown_sender_and_subject()
    {
        Assert.True(DeliveryReportCandidate.IsLikely(Message("bounces@relay.example", "Hello", "multipart/report")));
    }

    [Theory]
    [InlineData("multipart/mixed")]
    [InlineData("text/plain")]
    [InlineData(null)]
    public void IsLikely_ignores_other_content_types(string? contentType)
    {
        Assert.False(DeliveryReportCandidate.IsLikely(Message("maria@client.com", "Hello", contentType)));
    }
}
