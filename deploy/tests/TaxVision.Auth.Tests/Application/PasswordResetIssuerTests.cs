using BuildingBlocks.Messaging.AuthIntegrationEvents;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Credentials;
using TaxVision.Auth.Application.Credentials.Administration;
using TaxVision.Auth.Domain.Credentials;
using TaxVision.Auth.Domain.Users;
using Xunit;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Emisión por la oficina (03 §4.8, §6.6, §8.2): vida por origen, idempotencia de 60 s, tope por objetivo y
/// nunca RawToken para los orígenes de oficina.</summary>
public sealed class PasswordResetIssuerTests
{
    private readonly AccountSessionFixture _world = new();
    private readonly InMemoryCredentialTokenRepository _credentials = new();
    private readonly CredentialAdministrationOptions _options = new();
    private readonly Guid _actorId = Guid.NewGuid();

    private Task<BuildingBlocks.Results.Result<OfficeResetIssue>> IssueAsync(
        IAccountTokenProtector? protector = null,
        Guid? actorId = null,
        ResetOrigin origin = ResetOrigin.Administrative
    ) =>
        PasswordResetIssuer.IssueByOfficeAsync(
            _world.Admin,
            origin,
            actorId ?? _actorId,
            _options,
            _credentials,
            _world.TokenService,
            protector ?? new AccountTokenProtectionTests.CapturingAccountTokenProtector(),
            _world.Tenants,
            new NoUserLoginLock(),
            _world.Request.IpAddress,
            _world.Correlation.CorrelationId,
            CancellationToken.None
        );

    [Fact]
    public async Task An_administrative_link_lives_24_hours_and_travels_sealed()
    {
        var result = await IssueAsync();

        Assert.True(result.IsSuccess);
        var issue = result.Value;
        Assert.Equal(ResetLinkIssueOutcome.Issued, issue.Outcome);
        Assert.Equal(ResetOrigin.Administrative, issue.Token.Origin);
        Assert.Equal(_actorId, issue.Token.RequestedByUserId);
        var lifetime = issue.Token.ExpiresAtUtc - issue.Token.CreatedAtUtc;
        Assert.InRange(
            lifetime,
            TimeSpan.FromHours(24) - TimeSpan.FromSeconds(1),
            TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1)
        );

        var evt = Assert.IsType<PasswordResetRequestedIntegrationEvent>(issue.Event);
        Assert.Null(evt.RawToken);
        Assert.NotNull(evt.ProtectedToken);
        Assert.Equal("Administrative", evt.Origin);
        Assert.Equal("office_administrator", evt.RequestedByRole);
        Assert.Equal(evt.EventId, issue.Token.NotificationEventId);
    }

    [Fact]
    public async Task Without_a_sealing_key_nothing_is_issued()
    {
        var result = await IssueAsync(new NullAccountTokenProtector());

        Assert.True(result.IsFailure);
        Assert.Equal("Auth.CredentialAdmin.DeliveryUnavailable", result.Error.Code);
        Assert.Empty(_credentials.PasswordResets);
    }

    [Fact]
    public async Task A_repeat_within_60_seconds_returns_the_pending_link()
    {
        var first = await IssueAsync();
        var second = await IssueAsync();

        Assert.Equal(ResetLinkIssueOutcome.AlreadyPending, second.Value.Outcome);
        Assert.Same(first.Value.Token, second.Value.Token);
        Assert.Null(second.Value.Event);
        Assert.Single(_credentials.PasswordResets);
    }

    [Fact]
    public async Task Another_actor_supersedes_instead_of_repeating()
    {
        var first = await IssueAsync();
        var second = await IssueAsync(actorId: Guid.NewGuid());

        Assert.Equal(ResetLinkIssueOutcome.Issued, second.Value.Outcome);
        Assert.Equal(PasswordResetTokenStatus.Superseded, first.Value.Token.StatusAt(DateTime.UtcNow));
    }

    [Fact]
    public async Task The_daily_cap_per_target_stops_further_links()
    {
        for (var i = 0; i < _options.TargetDailyCap; i++)
            await _credentials.AddPasswordResetAsync(
                PasswordResetToken.Create(
                    _world.Admin.TenantId,
                    _world.Admin.Id,
                    $"old-{i}",
                    null,
                    TimeSpan.FromHours(24),
                    ResetOrigin.Administrative,
                    Guid.NewGuid()
                )
            );

        var result = await IssueAsync();

        Assert.True(result.IsFailure);
        Assert.Equal("Auth.CredentialAdmin.TargetRateLimited", result.Error.Code);
    }

    [Fact]
    public async Task Self_service_links_do_not_count_toward_the_cap()
    {
        for (var i = 0; i < _options.TargetDailyCap; i++)
            await _credentials.AddPasswordResetAsync(
                PasswordResetToken.Create(
                    _world.Admin.TenantId,
                    _world.Admin.Id,
                    $"self-{i}",
                    null,
                    TimeSpan.FromMinutes(30)
                )
            );

        Assert.True((await IssueAsync()).IsSuccess);
    }
}
