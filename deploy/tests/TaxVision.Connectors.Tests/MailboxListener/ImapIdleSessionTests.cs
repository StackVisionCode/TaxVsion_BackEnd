using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.MailboxListener.Imap;
using TaxVision.Connectors.MailboxListener.Observability;
using TaxVision.Connectors.MailboxListener.Options;
using TaxVision.Connectors.MailboxListener.Sessions;

namespace TaxVision.Connectors.Tests.MailboxListener;

public sealed class ImapIdleSessionTests
{
    private static readonly ActiveAccountRef Account = new(Guid.NewGuid(), ProviderCode.Imap, Guid.NewGuid());
    private static readonly MailboxConnectionInfo Connection = new("imap.example.com", 993, true, "user", "secret");

    private static ImapIdleSession CreateSession(
        ScriptedWatcherFactory factory,
        RecordingChangePublisher publisher,
        InMemoryMailboxListenerPresence? presence = null,
        MailboxConnectionInfo? connection = null,
        bool hasCredentials = true
    )
    {
        var options = new MailboxListenerOptions { ChangeDebounceSeconds = 0, ConnectDelayMilliseconds = 0 };
        var deps = new MailboxSessionDependencies(
            new FixedConnectionSource(hasCredentials ? connection ?? Connection : null),
            factory,
            new ConnectThrottle(Microsoft.Extensions.Options.Options.Create(options)),
            publisher,
            presence ?? new InMemoryMailboxListenerPresence(),
            new MailboxListenerMetrics(),
            new ManualTimeProvider()
        );
        return new ImapIdleSession(Account, deps, options, NullLogger.Instance);
    }

    [Fact]
    public async Task Listening_PublishesOnConnectAndOnEveryChange()
    {
        var watcher = new ScriptedMailboxWatcher();
        watcher.Waits.Enqueue(false);
        watcher.Waits.Enqueue(true);
        var publisher = new RecordingChangePublisher();
        var presence = new InMemoryMailboxListenerPresence();
        using var cts = new CancellationTokenSource();
        var run = CreateSession(new ScriptedWatcherFactory(watcher), publisher, presence).RunAsync(cts.Token);

        // Uno al conectar (lo que llegó sin escuchar) y otro por el cambio; la espera sin cambios no avisa.
        await publisher.WaitForChangesAsync(2);
        await cts.CancelAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(2, publisher.Changes.Count);
        Assert.All(publisher.Changes, id => Assert.Equal(Account.Id, id));
        Assert.True(await presence.IsListeningAsync(Account.Id));
        Assert.True(watcher.Disposed);
    }

    [Fact]
    public async Task RejectedLogin_PublishesAuthFailedOnceAndStops()
    {
        var watcher = new ScriptedMailboxWatcher
        {
            ConnectFailure = new MailboxAuthenticationException(new Exception()),
        };
        var factory = new ScriptedWatcherFactory(watcher);
        var publisher = new RecordingChangePublisher();

        await CreateSession(factory, publisher).RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(Account.Id, Assert.Single(publisher.AuthFailures));
        Assert.Empty(publisher.Changes);
        Assert.Single(factory.Created);
    }

    [Fact]
    public async Task MissingCredentials_StopsWithoutConnecting()
    {
        var factory = new ScriptedWatcherFactory();
        var publisher = new RecordingChangePublisher();

        await CreateSession(factory, publisher, hasCredentials: false)
            .RunAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Null(factory.Created.Single().ConnectedWith);
        Assert.Empty(publisher.Changes);
        Assert.Empty(publisher.AuthFailures);
    }

    [Fact]
    public async Task DroppedConnection_ReconnectsWithANewWatcher()
    {
        var broken = new ScriptedMailboxWatcher { ConnectFailure = new IOException("connection reset") };
        var healthy = new ScriptedMailboxWatcher();
        var factory = new ScriptedWatcherFactory(broken, healthy);
        var publisher = new RecordingChangePublisher();
        using var cts = new CancellationTokenSource();
        var run = CreateSession(factory, publisher).RunAsync(cts.Token);

        await publisher.WaitForChangesAsync(1);
        await cts.CancelAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(Connection, healthy.ConnectedWith);
        Assert.True(broken.Disposed);
        Assert.Empty(publisher.AuthFailures);
    }

    [Fact]
    public async Task PresenceFailure_DoesNotDropTheImapConnection()
    {
        var watcher = new ScriptedMailboxWatcher();
        watcher.Waits.Enqueue(true);
        var factory = new ScriptedWatcherFactory(watcher);
        var publisher = new RecordingChangePublisher();
        var presence = new InMemoryMailboxListenerPresence { FailOnMark = true };
        using var cts = new CancellationTokenSource();
        var run = CreateSession(factory, publisher, presence).RunAsync(cts.Token);

        await publisher.WaitForChangesAsync(2);
        await cts.CancelAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Single(factory.Created);
        Assert.True(presence.Marks >= 1);
    }

    [Fact]
    public void Backoff_GrowsAndStopsAtTheCap()
    {
        var first = ImapIdleSession.BackoffFor(1, maxBackoffMinutes: 10);
        var fourth = ImapIdleSession.BackoffFor(4, maxBackoffMinutes: 10);
        var hundredth = ImapIdleSession.BackoffFor(100, maxBackoffMinutes: 10);

        Assert.InRange(first.TotalSeconds, 4, 6);
        Assert.InRange(fourth.TotalSeconds, 32, 48);
        Assert.InRange(hundredth.TotalSeconds, 480, 720);
    }

    [Fact]
    public void ConnectionInfo_NeverPrintsTheCredentials()
    {
        var text = Connection.ToString();

        Assert.DoesNotContain("secret", text);
        Assert.DoesNotContain("user", text);
        Assert.Equal("imap.example.com:993", text);
    }
}
