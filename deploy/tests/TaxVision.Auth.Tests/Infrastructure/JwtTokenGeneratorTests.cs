using System.IdentityModel.Tokens.Jwt;
using BuildingBlocks.ActorTypeAuthorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Infrastructure.Security;

namespace TaxVision.Auth.Tests.Infrastructure;

public sealed class JwtTokenGeneratorTests
{
    private static readonly User Admin = User.Register(
        Guid.NewGuid(),
        "Ada",
        "Admin",
        "ada@example.com",
        "hash",
        UserActorType.TenantAdmin
    ).Value;

    [Fact]
    public void Workspace_tokens_keep_the_services_audience_and_carry_no_surface()
    {
        var token = Read(Generator().Generate(Admin, "UTC", Guid.NewGuid(), [], ["pwd"]).Token);

        Assert.Equal(new[] { "TaxVision.Services" }, token.Audiences);
        Assert.DoesNotContain(token.Claims, claim => claim.Type == ClaimNames.Surface);
        Assert.DoesNotContain(token.Claims, claim => claim.Type == ClaimNames.ReauthenticatedAt);
    }

    [Fact]
    public void Account_tokens_carry_the_surface_and_their_own_audience()
    {
        var sessionId = Guid.NewGuid();

        var token = Read(Generator().Generate(Admin, "UTC", sessionId, [], ["handoff"], SessionSurface.Account).Token);

        Assert.Equal(new[] { "TaxVision.Account" }, token.Audiences);
        Assert.Contains(
            token.Claims,
            claim => claim.Type == ClaimNames.Surface && claim.Value == AccessSurface.Account
        );
        Assert.Contains(token.Claims, claim => claim.Type == "sid" && claim.Value == sessionId.ToString());
    }

    [Fact]
    public void A_reauthentication_is_stamped_as_epoch_seconds()
    {
        var reauthenticatedAt = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);

        var token = Read(
            Generator()
                .Generate(Admin, "UTC", Guid.NewGuid(), [], ["pwd"], SessionSurface.Workspace, reauthenticatedAt)
                .Token
        );

        var claim = Assert.Single(token.Claims, claim => claim.Type == ClaimNames.ReauthenticatedAt);
        Assert.Equal(new DateTimeOffset(reauthenticatedAt).ToUnixTimeSeconds().ToString(), claim.Value);
    }

    private static JwtTokenGenerator Generator()
    {
        var options = Options.Create(
            new JwtOptions
            {
                Issuer = "TaxVision.Auth",
                Audience = "TaxVision.Services",
                Secret = "a-test-secret-that-is-long-enough-for-hs256",
            }
        );
        return new JwtTokenGenerator(options, new SigningKeyProvider(options, new TestHostEnvironment()));
    }

    private static JwtSecurityToken Read(string token) => new JwtSecurityTokenHandler().ReadJwtToken(token);

    private sealed class TestHostEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "TaxVision.Auth.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public string EnvironmentName { get; set; } = "Testing";
    }
}
