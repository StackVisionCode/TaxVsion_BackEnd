using TaxVision.Connectors.Application.Accounts;

namespace TaxVision.Connectors.Tests.Accounts;

public sealed class ConnectedEmailIdentityGuardTests
{
    [Fact]
    public void Ensure_ExactMatch_Succeeds()
    {
        var result = ConnectedEmailIdentityGuard.Ensure("office@example.com", "office@example.com");

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("Office@Example.com", "office@example.com")] // casing distinto
    [InlineData("  office@example.com  ", "office@example.com")] // espacios alrededor
    [InlineData("office@example.com", "  OFFICE@EXAMPLE.COM ")] // normaliza ambos lados
    public void Ensure_NormalizesLikeAuth_Succeeds(string mailbox, string initiator)
    {
        var result = ConnectedEmailIdentityGuard.Ensure(mailbox, initiator);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Ensure_DifferentMailbox_FailsWithMismatch()
    {
        var result = ConnectedEmailIdentityGuard.Ensure("someoneelse@example.com", "office@example.com");

        Assert.True(result.IsFailure);
        Assert.Equal("Connectors.EmailIdentity.Mismatch", result.Error.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Ensure_MissingInitiator_FailsWithMissingInitiator(string? initiator)
    {
        var result = ConnectedEmailIdentityGuard.Ensure("office@example.com", initiator);

        Assert.True(result.IsFailure);
        Assert.Equal("Connectors.EmailIdentity.MissingInitiator", result.Error.Code);
    }
}
