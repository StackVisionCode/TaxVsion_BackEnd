using BuildingBlocks.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Api.Controllers;
using TaxVision.Auth.Application.Credentials.Queries;
using TaxVision.Auth.Domain.Credentials;
using TaxVision.Auth.Domain.TenantDomains;
using Xunit;

namespace TaxVision.Auth.Tests.Application;

public sealed class ValidatePasswordResetTokenHandlerTests
{
    private readonly AccountSessionFixture _world = new();
    private readonly InMemoryCredentialTokenRepository _credentials = new();
    private readonly PredictableTokenService _tokens = new();
    private readonly FakeTenantDomainRepository _domains = new();

    public ValidatePasswordResetTokenHandlerTests() => SeedSubdomain("primary-office");

    [Fact]
    public async Task A_pending_link_is_valid_and_stays_usable()
    {
        var link = IssueLink(out var rawToken);

        var result = await ValidateAsync(rawToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("Valid", result.Value.Status);
        Assert.NotNull(result.Value.Tenant);
        Assert.True(link.IsUsable(DateTime.UtcNow));
        Assert.Equal(0, link.Attempts);
    }

    // Misma fuente que el link del correo: subdominio primario activo, no Tenant.SubDomain.
    [Fact]
    public async Task The_office_comes_from_the_primary_active_subdomain_like_the_email_link()
    {
        IssueLink(out var rawToken);

        var result = await ValidateAsync(rawToken);

        Assert.NotEqual("primary-office", _world.Tenant.SubDomain);
        Assert.Equal("primary-office", result.Value.Tenant!.SubDomain);
    }

    [Fact]
    public async Task Without_an_active_subdomain_the_link_is_invalid()
    {
        IssueLink(out var rawToken);

        var result = await ValidateAsync(rawToken, new FakeTenantDomainRepository());

        Assert.Equal("Invalid", result.Value.Status);
        Assert.Null(result.Value.Tenant);
    }

    [Fact]
    public async Task A_revoked_link_is_invalid()
    {
        var link = IssueLink(out var rawToken);
        link.Revoke(DateTime.UtcNow, ResetRevocationReason.PasswordChanged);

        var result = await ValidateAsync(rawToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("Invalid", result.Value.Status);
        Assert.Null(result.Value.Tenant);
    }

    // 03 §8.4: estados granulares solo cuando el hash coincide; el revocado por cambio de contraseña sigue Invalid.
    [Fact]
    public async Task A_used_link_reports_completed_with_its_office()
    {
        var link = IssueLink(out var rawToken);
        link.MarkUsed();

        var result = await ValidateAsync(rawToken);

        Assert.Equal("Completed", result.Value.Status);
        Assert.NotNull(result.Value.Tenant);
        Assert.Equal("SelfService", result.Value.Origin);
    }

    [Fact]
    public async Task A_link_cancelled_by_the_office_reports_cancelled()
    {
        var link = IssueLink(out var rawToken, ResetOrigin.Administrative);
        link.Revoke(DateTime.UtcNow, ResetRevocationReason.CancelledByOffice, Guid.NewGuid());

        var result = await ValidateAsync(rawToken);

        Assert.Equal("Cancelled", result.Value.Status);
        Assert.Equal("Administrative", result.Value.Origin);
    }

    [Fact]
    public async Task A_replaced_link_reports_superseded()
    {
        var link = IssueLink(out var rawToken);
        link.Revoke(DateTime.UtcNow, ResetRevocationReason.Superseded);

        Assert.Equal("Superseded", (await ValidateAsync(rawToken)).Value.Status);
    }

    [Fact]
    public async Task An_exhausted_link_reports_expired()
    {
        var link = IssueLink(out var rawToken);
        for (var i = 0; i < PasswordResetToken.MaxAttempts; i++)
            link.RegisterAttempt();

        Assert.Equal("Expired", (await ValidateAsync(rawToken)).Value.Status);
    }

    [Fact]
    public async Task An_expired_link_reports_expired()
    {
        IssueLink(out var rawToken, validity: TimeSpan.FromMinutes(-1));

        Assert.Equal("Expired", (await ValidateAsync(rawToken)).Value.Status);
    }

    [Fact]
    public async Task A_link_revoked_by_securing_the_account_is_invalid()
    {
        var link = IssueLink(out var rawToken);
        link.Revoke(DateTime.UtcNow, ResetRevocationReason.AccountSecured);

        var result = await ValidateAsync(rawToken);

        Assert.Equal("Invalid", result.Value.Status);
        Assert.Null(result.Value.Origin);
    }

    [Fact]
    public async Task A_link_of_an_inactive_account_is_invalid()
    {
        var link = IssueLink(out var rawToken);
        link.MarkUsed();
        _world.Admin.Deactivate(Guid.NewGuid(), DateTime.UtcNow);

        var result = await ValidateAsync(rawToken);

        Assert.Equal("Invalid", result.Value.Status);
        Assert.Null(result.Value.Tenant);
    }

    [Fact]
    public async Task A_pending_office_link_is_valid_and_says_its_origin()
    {
        IssueLink(out var rawToken, ResetOrigin.Administrative);

        var result = await ValidateAsync(rawToken);

        Assert.Equal("Valid", result.Value.Status);
        Assert.Equal("Administrative", result.Value.Origin);
    }

    // D-13 (PB-03): con el hash coincidente dice si la cuenta tiene MFA (12 o 15 caracteres); nunca en Invalid.
    [Fact]
    public async Task A_matching_link_says_whether_the_account_has_two_step_on()
    {
        IssueLink(out var withoutMfa);
        var off = await ValidateAsync(withoutMfa);
        _world.Admin.EnableMfa();
        IssueLink(out var withMfa);
        var on = await ValidateAsync(withMfa);
        var unknown = await ValidateAsync("raw-unknown");

        Assert.False(off.Value.MfaEnabled);
        Assert.True(on.Value.MfaEnabled);
        Assert.Null(unknown.Value.MfaEnabled);
    }

    [Fact]
    public async Task An_unknown_link_is_invalid()
    {
        var result = await ValidateAsync("raw-unknown");

        Assert.True(result.IsSuccess);
        Assert.Equal("Invalid", result.Value.Status);
    }

    // D-15: POST con el token en el body; el GET se conserva una versión. Ambos deciden lo mismo.
    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task Both_verbs_validate_the_same_way(string verb)
    {
        IssueLink(out var rawToken, ResetOrigin.Administrative);
        var bus = new FakeMessageBus
        {
            InvokeHandler = message =>
                ValidateAsync(((ValidatePasswordResetTokenQuery)message).Token).GetAwaiter().GetResult(),
        };
        var controller = new CredentialsController(bus);

        var response =
            verb == "GET"
                ? await controller.ValidateResetToken(rawToken, CancellationToken.None)
                : await controller.ValidateResetTokenByPost(
                    new CredentialsController.ValidateResetTokenRequest(rawToken),
                    CancellationToken.None
                );

        var body = Assert.IsType<PasswordResetValidationResponse>(Assert.IsType<OkObjectResult>(response).Value);
        Assert.Equal("Valid", body.Status);
        Assert.Equal("Administrative", body.Origin);
    }

    private PasswordResetToken IssueLink(
        out string rawToken,
        ResetOrigin origin = ResetOrigin.SelfService,
        TimeSpan? validity = null
    )
    {
        rawToken = _tokens.GenerateToken();
        var link = PasswordResetToken.Create(
            _world.Admin.TenantId,
            _world.Admin.Id,
            _tokens.Hash(rawToken),
            "127.0.0.1",
            validity ?? TimeSpan.FromMinutes(30),
            origin,
            origin == ResetOrigin.SelfService ? null : Guid.NewGuid()
        );
        _credentials.PasswordResets.Add(link);
        return link;
    }

    private Task<Result<PasswordResetValidationResponse>> ValidateAsync(
        string rawToken,
        FakeTenantDomainRepository? domains = null
    ) =>
        ValidatePasswordResetTokenHandler.Handle(
            new ValidatePasswordResetTokenQuery(rawToken),
            _credentials,
            _tokens,
            _world.Users,
            _world.Tenants,
            domains ?? _domains,
            CancellationToken.None
        );

    private void SeedSubdomain(string slug) =>
        _domains.Seed(
            TenantDomain
                .CreateSubdomain(
                    _world.Tenant.Id,
                    SubdomainSlug.Create(slug).Value,
                    "taxoffix.test",
                    Guid.NewGuid(),
                    DateTime.UtcNow
                )
                .Value
        );
}
