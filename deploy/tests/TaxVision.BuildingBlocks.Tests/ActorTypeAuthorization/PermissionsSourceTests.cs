using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Permissions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.ActorTypeAuthorization;

public sealed class JwtEmbeddedPermissionsSourceTests
{
    [Fact]
    public async Task Delegates_to_the_perm_claim_on_the_JWT()
    {
        var source = new JwtEmbeddedPermissionsSource();
        var principal = BuildPrincipal(new Claim(ClaimNames.Permission, "customers.view"));

        Assert.True(await source.HasPermissionAsync(principal, "customers.view"));
        Assert.False(await source.HasPermissionAsync(principal, "customers.delete"));
    }

    private static ClaimsPrincipal BuildPrincipal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "Test"));
}

public sealed class ProjectionPermissionsSourceTests
{
    [Fact]
    public async Task Returns_true_when_the_permission_is_in_the_projection_and_perm_v_matches()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var reader = new FakeProjectionReader(
            new UserPermissionsSnapshot(PermissionsVersion: 3, PermissionCodes: ["customers.view"])
        );
        var source = CreateSource(reader);
        var principal = BuildPrincipal(userId, tenantId, permVersion: 3);

        Assert.True(await source.HasPermissionAsync(principal, "customers.view"));
        Assert.False(await source.HasPermissionAsync(principal, "customers.delete"));
    }

    [Fact]
    public async Task Throws_TokenStale_when_the_JWT_perm_v_is_behind_the_projection()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var reader = new FakeProjectionReader(new UserPermissionsSnapshot(PermissionsVersion: 5, PermissionCodes: []));
        var source = CreateSource(reader);
        var principal = BuildPrincipal(userId, tenantId, permVersion: 4);

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            source.HasPermissionAsync(principal, "customers.view")
        );
        Assert.Equal("Auth.TokenStale", ex.Message);
    }

    [Fact]
    public async Task Fails_closed_and_logs_a_warning_when_no_projection_snapshot_exists_yet()
    {
        var reader = new FakeProjectionReader(snapshot: null);
        var logger = new RecordingLogger<ProjectionPermissionsSource>();
        var source = CreateSource(reader, logger);
        var principal = BuildPrincipal(Guid.NewGuid(), Guid.NewGuid(), permVersion: 1);

        Assert.False(await source.HasPermissionAsync(principal, "customers.view"));
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Fails_closed_when_the_JWT_is_missing_userId_or_tenantId_claims()
    {
        var reader = new FakeProjectionReader(
            new UserPermissionsSnapshot(PermissionsVersion: 1, PermissionCodes: ["customers.view"])
        );
        var source = CreateSource(reader);
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.False(await source.HasPermissionAsync(principal, "customers.view"));
    }

    [Fact]
    public async Task Bypasses_the_projection_entirely_for_PlatformAdmin()
    {
        var reader = new FakeProjectionReader(snapshot: null);
        var source = CreateSource(reader);
        var principal = BuildPrincipal(Guid.NewGuid(), Guid.NewGuid(), permVersion: 1, isPlatformAdmin: true);

        Assert.True(await source.HasPermissionAsync(principal, "anything.at.all"));
        Assert.Equal(0, reader.CallCount);
    }

    [Fact]
    public async Task Bypasses_the_projection_for_Service_actor_type_and_reads_the_perm_claim_directly()
    {
        // RBAC Fase 7.5 — tokens M2M nunca tienen fila de proyección (su sub es un GUID sintético
        // nunca sincronizado por UserRolesChangedIntegrationEvent) ni perm_v; sin este bypass
        // fallarían cerrados siempre en modo Projection.
        var reader = new FakeProjectionReader(snapshot: null);
        var source = CreateSource(reader);
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim(ClaimNames.ActorType, "Service"), new Claim(ClaimNames.Permission, "scribe.render")],
                authenticationType: "Test"
            )
        );

        Assert.True(await source.HasPermissionAsync(principal, "scribe.render"));
        Assert.False(await source.HasPermissionAsync(principal, "scribe.other"));
        Assert.Equal(0, reader.CallCount);
    }

    [Fact]
    public async Task Caches_the_snapshot_for_30_seconds_so_a_burst_of_requests_hits_the_reader_once()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var reader = new FakeProjectionReader(
            new UserPermissionsSnapshot(PermissionsVersion: 1, PermissionCodes: ["customers.view"])
        );
        var source = CreateSource(reader);
        var principal = BuildPrincipal(userId, tenantId, permVersion: 1);

        for (var i = 0; i < 5; i++)
            await source.HasPermissionAsync(principal, "customers.view");

        Assert.Equal(1, reader.CallCount);
    }

    private static ProjectionPermissionsSource CreateSource(
        FakeProjectionReader reader,
        ILogger<ProjectionPermissionsSource>? logger = null
    ) =>
        new(
            reader,
            new MemoryCache(new MemoryCacheOptions()),
            logger ?? new RecordingLogger<ProjectionPermissionsSource>()
        );

    private static ClaimsPrincipal BuildPrincipal(
        Guid userId,
        Guid tenantId,
        int permVersion,
        bool isPlatformAdmin = false
    )
    {
        List<Claim> claims =
        [
            new("sub", userId.ToString()),
            new(ClaimNames.TenantId, tenantId.ToString()),
            new("perm_v", permVersion.ToString()),
        ];
        if (isPlatformAdmin)
            claims.Add(new Claim(ClaimTypes.Role, "PlatformAdmin"));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));
    }

    private sealed class FakeProjectionReader(UserPermissionsSnapshot? snapshot) : IUserPermissionsProjectionReader
    {
        public int CallCount { get; private set; }

        public Task<UserPermissionsSnapshot?> GetSnapshotAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        )
        {
            CallCount++;
            return Task.FromResult(snapshot);
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => Entries.Add((logLevel, formatter(state, exception)));
    }
}
