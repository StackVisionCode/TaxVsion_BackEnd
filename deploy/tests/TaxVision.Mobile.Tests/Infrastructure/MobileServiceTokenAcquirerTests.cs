using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TaxVision.Mobile.Infrastructure.Permissions;

namespace TaxVision.Mobile.Tests.Infrastructure;

/// <summary>Token M2M de Mobile: sin credenciales no se llama a Auth y queda un aviso en el log.</summary>
public sealed class MobileServiceTokenAcquirerTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "test-only")]
    [InlineData("mobile-worker", "")]
    [InlineData("mobile-worker", "   ")]
    public async Task Without_configuration_it_returns_null_and_warns(string? clientId, string? secret)
    {
        var auth = new CountingAuth();
        var log = new ListLogger();
        var acquirer = Acquirer(auth, log, clientId, secret);

        var token = await acquirer.GetTokenAsync(Guid.NewGuid());

        Assert.Null(token);
        Assert.Equal(0, auth.Requests);
        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("ServiceAuthClient", entry.Message);
    }

    [Fact]
    public async Task With_configuration_it_asks_auth_and_does_not_warn()
    {
        var auth = new CountingAuth();
        var log = new ListLogger();
        var acquirer = Acquirer(auth, log, "mobile-worker", "test-only");

        var token = await acquirer.GetTokenAsync(Guid.NewGuid());

        Assert.Equal("token-1", token);
        Assert.Empty(log.Entries);
    }

    private static MobileServiceTokenAcquirer Acquirer(
        CountingAuth auth,
        ListLogger log,
        string? clientId,
        string? secret
    ) =>
        new(
            new HttpClient(auth) { BaseAddress = new Uri("https://auth.test/") },
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ServiceAuthClient:ClientId"] = clientId,
                        ["ServiceAuthClient:ClientSecret"] = secret,
                    }
                )
                .Build(),
            log
        );

    private sealed class CountingAuth : HttpMessageHandler
    {
        private int _requests;

        public int Requests => _requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var n = Interlocked.Increment(ref _requests);
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $$"""{"accessToken":"token-{{n}}","expiresInSeconds":600}""",
                        System.Text.Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
        }
    }

    private sealed class ListLogger : ILogger<MobileServiceTokenAcquirer>
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
