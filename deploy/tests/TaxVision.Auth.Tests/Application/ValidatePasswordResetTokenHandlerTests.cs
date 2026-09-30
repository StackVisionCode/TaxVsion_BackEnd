using BuildingBlocks.Results;
using TaxVision.Auth.Application.Credentials.Queries;
using TaxVision.Auth.Domain.Credentials;
using Xunit;

namespace TaxVision.Auth.Tests.Application;

public sealed class ValidatePasswordResetTokenHandlerTests
{
    private readonly AccountSessionFixture _world = new();
    private readonly InMemoryCredentialTokenRepository _credentials = new();
    private readonly PredictableTokenService _tokens = new();

    [Fact]
    public async Task A_pending_link_is_valid_and_stays_usable()
    {
        var link = IssueLink(out var rawToken);

        var result = await ValidateAsync(rawToken);

        Assert.True(result.IsSuccess);
        Assert.True(link.IsUsable(DateTime.UtcNow));
        Assert.Equal(0, link.Attempts);
    }

    [Fact]
    public async Task A_revoked_link_is_invalid()
    {
        var link = IssueLink(out var rawToken);
        link.Revoke(DateTime.UtcNow);

        var result = await ValidateAsync(rawToken);

        Assert.Equal("Auth.InvalidResetToken", result.Error.Code);
    }

    [Fact]
    public async Task A_used_link_is_invalid()
    {
        var link = IssueLink(out var rawToken);
        link.MarkUsed();

        var result = await ValidateAsync(rawToken);

        Assert.Equal("Auth.InvalidResetToken", result.Error.Code);
    }

    [Fact]
    public async Task An_unknown_link_is_invalid()
    {
        var result = await ValidateAsync("raw-unknown");

        Assert.Equal("Auth.InvalidResetToken", result.Error.Code);
    }

    private PasswordResetToken IssueLink(out string rawToken)
    {
        rawToken = _tokens.GenerateToken();
        var link = PasswordResetToken.Create(
            _world.Admin.TenantId,
            _world.Admin.Id,
            _tokens.Hash(rawToken),
            "127.0.0.1",
            TimeSpan.FromMinutes(30)
        );
        _credentials.PasswordResets.Add(link);
        return link;
    }

    private Task<Result> ValidateAsync(string rawToken) =>
        ValidatePasswordResetTokenHandler.Handle(
            new ValidatePasswordResetTokenQuery(rawToken),
            _credentials,
            _tokens,
            _world.Users,
            CancellationToken.None
        );
}
