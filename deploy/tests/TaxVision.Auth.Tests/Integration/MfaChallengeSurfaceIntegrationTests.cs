using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Domain.Mfa;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Infrastructure.Persistence.Repositories;

namespace TaxVision.Auth.Tests.Integration;

/// <summary>Contra SQL Server: el desafío guarda la superficie que lo pidió y los anteriores quedan como web.</summary>
public sealed class MfaChallengeSurfaceIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private AuthTestDatabase _database = null!;

    public async Task InitializeAsync() => _database = await AuthTestDatabase.CreateAsync();

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Theory]
    [InlineData(SessionSurface.Mobile)]
    [InlineData(SessionSurface.Workspace)]
    public async Task A_challenge_comes_back_with_the_surface_that_asked_for_it(SessionSurface surface)
    {
        var ticketHash = Guid.NewGuid().ToString("N");
        await using (var db = _database.NewContext(_tenantId))
        {
            await new MfaRepository(db).AddChallengeAsync(
                MfaChallenge.Create(_tenantId, Guid.NewGuid(), null, ticketHash, null, TimeSpan.FromMinutes(5), surface)
            );
            await db.SaveChangesAsync();
        }

        await using var fresh = _database.NewContext(_tenantId);
        var stored = await new MfaRepository(fresh).GetChallengeByTicketHashAsync(ticketHash);

        Assert.Equal(surface, stored!.Surface);
    }

    [Fact]
    public async Task A_row_written_without_the_column_is_a_web_challenge()
    {
        var ticketHash = Guid.NewGuid().ToString("N");
        await using var db = _database.NewContext(_tenantId);
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO [MfaChallenges] ([Id], [TenantId], [UserId], [LoginTicketHash], [Attempts], [CreatedAtUtc], [ExpiresAtUtc])
            VALUES ({0}, {1}, {2}, {3}, 0, SYSUTCDATETIME(), DATEADD(MINUTE, 5, SYSUTCDATETIME()))
            """,
            Guid.NewGuid(),
            _tenantId,
            Guid.NewGuid(),
            ticketHash
        );

        var stored = await new MfaRepository(db).GetChallengeByTicketHashAsync(ticketHash);

        Assert.Equal(SessionSurface.Workspace, stored!.Surface);
    }
}
