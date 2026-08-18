using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Feeds.Abstractions;
using TaxVision.Calendar.Application.Feeds.Commands;
using TaxVision.Calendar.Application.Feeds.Queries;
using TaxVision.Calendar.Application.Observability;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Feeds;
using Xunit;

namespace TaxVision.Calendar.Tests.Feeds;

/// <summary>
/// La URL del feed no lleva sesión, así que lo único que la protege es que el token sea largo,
/// revocable, y que fallar no diga por qué.
/// </summary>
public sealed class CalendarFeedTokenTests
{
    private static readonly Guid Tenant = Guid.Parse("d4879234-7370-4b58-b49c-094bd7c04847");
    private static readonly Guid User = Guid.Parse("2b91f0c4-1111-4222-8333-444455556666");

    /// <summary>256 bits en base64url: 43 caracteres. Menos que eso se enumera.</summary>
    [Fact]
    public void The_token_carries_at_least_32_bytes_of_entropy()
    {
        var token = FeedToken.Create();

        Assert.Equal(43, token.Value.Length);
        Assert.Equal(32, token.Hash.Length);
        Assert.DoesNotContain('+', token.Value);
        Assert.DoesNotContain('/', token.Value);
    }

    [Fact]
    public async Task Issuing_a_second_token_revokes_the_first()
    {
        var tokens = new InMemoryTokens();
        var first = await IssueAsync(tokens);
        var second = await IssueAsync(tokens);

        Assert.NotEqual(first.Url, second.Url);
        Assert.Single(tokens.All, t => t.IsActive);
    }

    /// <summary>
    /// Los tres motivos —revocado, inexistente y de otro usuario— responden igual. Distinguirlos
    /// convierte la URL en un oráculo de qué usuarios existen en el tenant.
    /// </summary>
    [Theory]
    [InlineData(Reason.Revoked)]
    [InlineData(Reason.Unknown)]
    [InlineData(Reason.WrongUser)]
    public async Task Every_failure_looks_the_same(Reason reason)
    {
        var tokens = new InMemoryTokens();
        var issued = await IssueAsync(tokens);
        var plain = PlainOf(issued.Url);

        var (userId, value) = reason switch
        {
            Reason.Revoked => Revoke(tokens, User, plain),
            Reason.Unknown => (User, FeedToken.Create().Value),
            _ => (Guid.NewGuid(), plain),
        };

        var result = await GetCalendarFeedHandler.Handle(
            new GetCalendarFeedQuery(userId, value),
            tokens,
            new EmptyAppointments(),
            new RecordingCache(),
            new NoOpUnitOfWork(),
            new NoOpMetrics(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FeedErrors.NotFound.Code, result.Error.Code);
    }

    [Fact]
    public async Task A_live_token_returns_the_calendar_and_records_the_visit()
    {
        var tokens = new InMemoryTokens();
        var issued = await IssueAsync(tokens);

        var result = await GetCalendarFeedHandler.Handle(
            new GetCalendarFeedQuery(User, PlainOf(issued.Url)),
            tokens,
            new EmptyAppointments(),
            new RecordingCache(),
            new NoOpUnitOfWork(),
            new NoOpMetrics(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Contains("BEGIN:VCALENDAR", result.Value);
        Assert.NotNull(tokens.All[0].LastAccessedAtUtc);
    }

    /// <summary>
    /// El camino en vivo guarda la copia que después sirve el respaldo. La clave sale del token y de
    /// nada más, así que el controller puede calcularla sin tocar la base — que es todo el punto.
    /// </summary>
    [Fact]
    public async Task A_successful_read_leaves_the_copy_that_the_fallback_will_serve()
    {
        var tokens = new InMemoryTokens();
        var cache = new RecordingCache();
        var plain = PlainOf((await IssueAsync(tokens, cache)).Url);

        await ReadAsync(tokens, cache, plain, new EmptyAppointments());

        var copy = Assert.Single(cache.Entries);
        Assert.Equal(CacheKey.For(plain), copy.Key);
        Assert.Contains("BEGIN:VCALENDAR", copy.Value);
    }

    /// <summary>
    /// Con la base caída el handler lanza a propósito: el respaldo vive en el controller, porque la
    /// transacción de Wolverine se abre antes del cuerpo del handler y un catch acá nunca vería el
    /// fallo de conexión.
    /// </summary>
    [Fact]
    public async Task With_the_database_down_the_handler_lets_the_failure_through()
    {
        var tokens = new InMemoryTokens();
        var cache = new RecordingCache();
        var plain = PlainOf((await IssueAsync(tokens, cache)).Url);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ReadAsync(tokens, cache, plain, new BrokenAppointments())
        );
    }

    /// <summary>
    /// Revocar tiene que matar también la copia. Si no, el botón de revocar no sirve para nada durante
    /// una caída — que es justo cuando alguien lo aprieta con prisa.
    /// </summary>
    [Fact]
    public async Task Revoking_also_drops_the_cached_copy()
    {
        var tokens = new InMemoryTokens();
        var cache = new RecordingCache();
        var plain = PlainOf((await IssueAsync(tokens, cache)).Url);

        await ReadAsync(tokens, cache, plain, new EmptyAppointments());
        Assert.NotEmpty(cache.Entries);

        await RevokeFeedTokenHandler.Handle(
            new RevokeFeedTokenCommand(Tenant, User),
            tokens,
            cache,
            new NoOpUnitOfWork(),
            CancellationToken.None
        );

        Assert.Empty(cache.Entries);
    }

    private static Task<BuildingBlocks.Results.Result<string>> ReadAsync(
        InMemoryTokens tokens,
        RecordingCache cache,
        string plain,
        IAppointmentRepository appointments
    ) =>
        GetCalendarFeedHandler.Handle(
            new GetCalendarFeedQuery(User, plain),
            tokens,
            appointments,
            cache,
            new NoOpUnitOfWork(),
            new NoOpMetrics(),
            CancellationToken.None
        );

    private sealed class BrokenAppointments : IAppointmentRepository
    {
        public Task<BuildingBlocks.Results.Result<Appointment>> GetByIdAsync(
            Guid tenantId,
            Guid appointmentId,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("la base no responde");

        public Task<IReadOnlyList<Appointment>> ListForRangeAsync(
            Guid tenantId,
            DateTime rangeStartUtc,
            DateTime rangeEndUtc,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("la base no responde");

        public Task<IReadOnlyList<Appointment>> ListForUserRangeAsync(
            Guid tenantId,
            Guid userId,
            DateTime rangeStartUtc,
            DateTime rangeEndUtc,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("la base no responde");

        public void Add(Appointment appointment) { }

        public void Remove(Appointment appointment) { }
    }

    public enum Reason
    {
        Revoked,
        Unknown,
        WrongUser,
    }

    private static (Guid, string) Revoke(InMemoryTokens tokens, Guid userId, string plain)
    {
        tokens.All[0].Revoke(DateTime.UtcNow);
        return (userId, plain);
    }

    private static string PlainOf(string url) => url[(url.LastIndexOf('/') + 1)..].Replace(".ics", string.Empty);

    private static async Task<IssuedFeedToken> IssueAsync(InMemoryTokens tokens, RecordingCache? cache = null) =>
        (
            await IssueFeedTokenHandler.Handle(
                new IssueFeedTokenCommand(Tenant, User),
                tokens,
                cache ?? new RecordingCache(),
                new NoOpUnitOfWork(),
                CancellationToken.None
            )
        ).Value;

    private sealed class InMemoryTokens : ICalendarFeedTokenRepository
    {
        public List<CalendarFeedToken> All { get; } = [];

        public Task<CalendarFeedToken?> FindActiveForUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult(All.Find(t => t.TenantId == tenantId && t.UserId == userId && t.IsActive));

        public Task<CalendarFeedToken?> FindByHashAsync(byte[] tokenHash, CancellationToken ct = default) =>
            Task.FromResult(All.Find(t => t.TokenHash.AsSpan().SequenceEqual(tokenHash)));

        public void Add(CalendarFeedToken token) => All.Add(token);
    }

    private sealed class EmptyAppointments : IAppointmentRepository
    {
        public Task<BuildingBlocks.Results.Result<Appointment>> GetByIdAsync(
            Guid tenantId,
            Guid appointmentId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<Appointment>> ListForRangeAsync(
            Guid tenantId,
            DateTime rangeStartUtc,
            DateTime rangeEndUtc,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<Appointment>>([]);

        public Task<IReadOnlyList<Appointment>> ListForUserRangeAsync(
            Guid tenantId,
            Guid userId,
            DateTime rangeStartUtc,
            DateTime rangeEndUtc,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<Appointment>>([]);

        public void Add(Appointment appointment) { }

        public void Remove(Appointment appointment) { }
    }

    private sealed class RecordingCache : ICalendarFeedCache
    {
        public Dictionary<string, string> Entries { get; } = [];

        public Task<string?> GetAsync(string tokenHashHex, CancellationToken ct = default) =>
            Task.FromResult(Entries.GetValueOrDefault(tokenHashHex));

        public Task SetAsync(string tokenHashHex, string ics, CancellationToken ct = default)
        {
            Entries[tokenHashHex] = ics;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string tokenHashHex, CancellationToken ct = default)
        {
            Entries.Remove(tokenHashHex);
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class NoOpMetrics : ICalendarMetrics
    {
        public void RecordCreated(bool isRecurring) { }

        public void RecordRescheduled(bool isRecurring) { }

        public void RecordCancelled(bool isRecurring) { }

        public void RecordExpansionDuration(double milliseconds, int seriesCount) { }

        public void RecordConflictDetected(bool blocked) { }

        public void RecordIcsFeedRequest(bool found) { }

        public void RecordIcsFeedStale() { }
    }
}
