using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Tests.Application.Offboarding;
using Xunit;

namespace TaxVision.Signature.Tests.Domain;

/// <summary>
/// P20 (TZ07-H9): la carpeta del año de los archivos de una solicitud es el año local de su zona (la que se congeló
/// al enviar). Sin zona, el año UTC de siempre.
/// </summary>
public sealed class SignatureRequestOfficeYearTests
{
    // 03:00Z del 1-ene-2027 = 21:00 del 31-dic-2026 en Chicago.
    private static readonly DateTime NewYearUtc = new(2027, 1, 1, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_year_is_the_local_year_of_the_request_zone()
    {
        var request = SentFrom("America/Chicago");

        Assert.Equal(2026, request.OfficeYearOf(NewYearUtc));
    }

    [Fact]
    public void Without_a_zone_the_year_is_the_utc_year()
    {
        var request = PreparerRequests.Draft(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(2027, request.OfficeYearOf(NewYearUtc));
    }

    private static SignatureRequest SentFrom(string zone)
    {
        var request = PreparerRequests.Draft(Guid.NewGuid(), Guid.NewGuid());
        var document = request.Documents.Single();
        var signer = request
            .AddSigner(
                TestSignatureLimits.Default,
                SignerEmail.Create("client@example.com").Value,
                SignerFullName.Create("Client Number").Value,
                null
            )
            .Value;
        request.PlaceField(
            TestSignatureLimits.MaxFieldsPerSigner,
            signer.Id,
            document.Id,
            SignatureFieldKind.Signature,
            FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value,
            null,
            false
        );
        request.AttachVerifiedDocument(document.Id, VerifiedDocument.Create(PreparerRequests.Hash('a'), 50).Value);
        Assert.True(request.Send(DateTime.UtcNow, zone).IsSuccess);
        return request;
    }
}
