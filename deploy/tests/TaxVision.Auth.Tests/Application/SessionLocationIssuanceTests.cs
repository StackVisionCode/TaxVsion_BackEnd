using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Sessions;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Infrastructure.Security;

namespace TaxVision.Auth.Tests.Application;

/// <summary>La ubicación se resuelve una vez, al crear la sesión; el refresh no la toca.</summary>
public sealed class SessionLocationIssuanceTests
{
    private static readonly SessionLocation MountainView = SessionLocation
        .Create("US", "California", "Mountain View", 37.42, -122.08, DateTime.UtcNow)
        .Value;

    private readonly InMemorySessionRepository _sessions = new();

    private readonly User _user = User.Register(
        Guid.NewGuid(),
        "Ana",
        "Ruiz",
        "ana@example.com",
        "hashed:x",
        UserActorType.TenantEmployee
    ).Value;

    [Fact]
    public async Task A_new_session_keeps_the_resolved_location()
    {
        var resolver = new FixedResolver(MountainView);

        await Issuer(resolver).StartSessionAsync(_user, "UTC", [], ["pwd"], "Chrome", SessionSurface.Workspace);

        Assert.Same(MountainView, Assert.Single(_sessions.Sessions).Location);
        Assert.Equal(["127.0.0.1"], resolver.Asked);
    }

    [Fact]
    public async Task Without_a_location_the_session_is_still_created()
    {
        await Issuer(new FixedResolver(null))
            .StartSessionAsync(_user, "UTC", [], ["pwd"], "Chrome", SessionSurface.Workspace);

        Assert.Null(Assert.Single(_sessions.Sessions).Location);
    }

    [Fact]
    public async Task A_refresh_does_not_look_up_or_change_the_location()
    {
        var resolver = new FixedResolver(MountainView);
        var issuer = Issuer(resolver);
        await issuer.StartSessionAsync(_user, "UTC", [], ["pwd"], "Chrome", SessionSurface.Workspace);
        var session = Assert.Single(_sessions.Sessions);
        resolver.Next = SessionLocation.Create("AU", null, "Sydney", -33.87, 151.21, DateTime.UtcNow).Value;

        await issuer.RotateAsync(_sessions.Tokens.Single(), session, _user, "UTC", [], ["pwd"]);

        Assert.Same(MountainView, session.Location);
        Assert.Single(resolver.Asked);
    }

    private AuthSessionIssuer Issuer(ISessionLocationResolver resolver) =>
        new(
            _sessions,
            new PredictableTokenService(),
            new CapturingJwtTokenGenerator(),
            new FakeRequestContext(),
            Options.Create(new RefreshTokenOptions()),
            new NoMfaRepository(),
            Options.Create(new MfaOptions { Enforced = false }),
            resolver
        );

    private sealed class FixedResolver(SessionLocation? location) : ISessionLocationResolver
    {
        public SessionLocation? Next { get; set; } = location;
        public List<string?> Asked { get; } = [];

        public SessionLocation? Resolve(string? ipAddress)
        {
            Asked.Add(ipAddress);
            return Next;
        }
    }
}
