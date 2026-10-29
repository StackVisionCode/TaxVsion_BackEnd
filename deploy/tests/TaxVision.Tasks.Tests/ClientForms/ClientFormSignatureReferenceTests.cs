using TaxVision.Tasks.ClientForms.Application.Actions;

namespace TaxVision.Tasks.Tests.ClientForms;

// P-18 — la clave de correlación "{requestId}:r{n}" que viaja en la ExternalReference de Signature.
public sealed class ClientFormSignatureReferenceTests
{
    [Fact]
    public void BuildSourceId_then_TryParse_roundtrips()
    {
        var requestId = Guid.NewGuid();
        var sourceId = ClientFormSignatureReference.BuildSourceId(requestId, 3);

        Assert.True(ClientFormSignatureReference.TryParse(sourceId, out var parsedId, out var revision));
        Assert.Equal(requestId, parsedId);
        Assert.Equal(3, revision);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not-a-reference")]
    [InlineData("deadbeef:rx")]
    public void TryParse_rejects_malformed_source_ids(string? sourceId)
    {
        Assert.False(ClientFormSignatureReference.TryParse(sourceId, out _, out _));
    }

    [Fact]
    public void Source_constants_match_the_contract()
    {
        Assert.Equal("tasks.client_forms", ClientFormSignatureReference.SourceService);
        Assert.Equal("ClientFormRevision", ClientFormSignatureReference.SourceType);
    }
}
