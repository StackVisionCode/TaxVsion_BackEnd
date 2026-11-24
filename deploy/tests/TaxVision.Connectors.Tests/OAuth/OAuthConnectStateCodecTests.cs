using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Infrastructure.OAuth;

namespace TaxVision.Connectors.Tests.OAuth;

public sealed class OAuthConnectStateCodecTests
{
    [Fact]
    public void Encode_RoundTripsEveryField()
    {
        var original = new OAuthConnectState(
            Guid.NewGuid(),
            ProviderCode.Graph,
            Guid.NewGuid(),
            "owner@example.test",
            "http://localhost:4200",
            AsOffice: true,
            InitiatorIsPlatformAdmin: true
        );

        var decoded = OAuthConnectStateCodec.Decode(OAuthConnectStateCodec.Encode(original));

        Assert.Equal(original, decoded);
    }

    // Un valor con '|' ya no puede correr los campos: la oficina y PlatformAdmin no se inyectan.
    [Fact]
    public void Encode_ValueWithPipes_DoesNotShiftTheFlags()
    {
        var original = new OAuthConnectState(
            Guid.NewGuid(),
            ProviderCode.Gmail,
            Guid.NewGuid(),
            "owner@example.test",
            "https://x|1|1"
        );

        var decoded = OAuthConnectStateCodec.Decode(OAuthConnectStateCodec.Encode(original));

        Assert.NotNull(decoded);
        Assert.False(decoded!.AsOffice);
        Assert.False(decoded.InitiatorIsPlatformAdmin);
        Assert.Equal("https://x|1|1", decoded.ReturnOrigin);
    }

    [Fact]
    public void Decode_LegacyPipeValue_StillReadsDuringTheDeploy()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var decoded = OAuthConnectStateCodec.Decode(
            $"{tenantId:N}|Gmail|{userId:N}|owner@example.test|https://manfer.taxoffix.io|1|0"
        );

        Assert.NotNull(decoded);
        Assert.Equal(tenantId, decoded!.TenantId);
        Assert.Equal("https://manfer.taxoffix.io", decoded.ReturnOrigin);
        Assert.True(decoded.AsOffice);
        Assert.False(decoded.InitiatorIsPlatformAdmin);
    }

    [Fact]
    public void Decode_LegacyValueWithExtraFields_IsRejected()
    {
        var decoded = OAuthConnectStateCodec.Decode(
            $"{Guid.NewGuid():N}|Gmail|{Guid.NewGuid():N}|owner@example.test|https://x|1|1|0|0"
        );

        Assert.Null(decoded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{not json")]
    [InlineData("{\"TenantId\":\"nope\"}")]
    public void Decode_Garbage_ReturnsNull(string value)
    {
        Assert.Null(OAuthConnectStateCodec.Decode(value));
    }
}
