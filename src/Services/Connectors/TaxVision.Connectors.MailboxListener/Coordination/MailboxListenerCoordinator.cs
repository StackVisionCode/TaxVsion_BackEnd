using Microsoft.Extensions.Options;
using TaxVision.Connectors.Application.Abstractions;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.MailboxListener.Options;
using TaxVision.Connectors.MailboxListener.Sessions;

namespace TaxVision.Connectors.MailboxListener.Coordination;

/// <summary>
/// Reparte las cuentas IMAP activas entre instancias con un lease en Redis por cuenta: cada vuelta
/// renueva los leases propios, suelta las cuentas que ya no están activas y toma nuevas hasta el tope.
/// Un solo hilo (el del servicio) lo llama, así que su estado no necesita locks.
/// </summary>
public sealed class MailboxListenerCoordinator(
    IServiceScopeFactory scopeFactory,
    IDistributedLock distributedLock,
    IMailboxSessionFactory sessionFactory,
    IOptions<MailboxListenerOptions> options,
    TimeProvider time,
    ILogger<MailboxListenerCoordinator> logger
)
{
    private const int PageSize = 500;

    /// <summary>Una sesión que terminó sola (login rechazado, sin credenciales) no se reintenta enseguida.</summary>
    public static readonly TimeSpan EndedCooldown = TimeSpan.FromMinutes(30);

    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);

    private readonly CancellationTokenSource _shutdown = new();
    private readonly Dictionary<Guid, RunningSession> _sessions = [];
    private readonly Dictionary<Guid, DateTimeOffset> _cooldownUntil = [];

    public int SessionCount => _sessions.Count;

    public bool IsListening(Guid accountId) => _sessions.ContainsKey(accountId);

    public async Task TickAsync(CancellationToken ct)
    {
        await ReapEndedAsync();
        await RenewLeasesAsync(ct);
        var accounts = await LoadImapAccountsAsync(ct);
        await ReleaseInactiveAsync(accounts);
        await ClaimNewAsync(accounts, ct);
    }

    public async Task StopAllAsync()
    {
        await _shutdown.CancelAsync();
        await Task.WhenAll(_sessions.Values.Select(StopAsync));
        _sessions.Clear();
    }

    private async Task ReapEndedAsync()
    {
        foreach (var (accountId, running) in _sessions.Where(s => s.Value.Task.IsCompleted).ToList())
        {
            if (running.Task.IsFaulted)
                logger.LogError(running.Task.Exception, "Mailbox listener session for {AccountId} crashed", accountId);

            _sessions.Remove(accountId);
            _cooldownUntil[accountId] = time.GetUtcNow() + EndedCooldown;
            await StopAsync(running);
        }
    }

    private async Task RenewLeasesAsync(CancellationToken ct)
    {
        var ttl = LeaseTtl;
        foreach (var (accountId, running) in _sessions.ToList())
        {
            bool stillOurs;
            try
            {
                stillOurs = await running.Lease.ExtendAsync(ttl, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Redis caído un momento: se sigue escuchando y se renueva en la próxima vuelta.
                logger.LogWarning(
                    "Mailbox listener could not renew lease for {AccountId}: {Error}",
                    accountId,
                    ex.GetType().Name
                );
                continue;
            }

            if (stillOurs)
                continue;

            logger.LogInformation("Mailbox listener lost lease for {AccountId}; another instance has it", accountId);
            _sessions.Remove(accountId);
            await StopAsync(running);
        }
    }

    private async Task<HashSet<ActiveAccountRef>> LoadImapAccountsAsync(CancellationToken ct)
    {
        var accounts = new HashSet<ActiveAccountRef>();
        Guid? afterId = null;
        while (true)
        {
            IReadOnlyList<ActiveAccountRef> page;
            await using (var scope = scopeFactory.CreateAsyncScope())
            {
                var repository = scope.ServiceProvider.GetRequiredService<ITenantEmailAccountRepository>();
                page = await repository.ListActivePageAsync(afterId, PageSize, ct);
            }

            if (page.Count == 0)
                return accounts;

            afterId = page[^1].Id;
            foreach (var account in page)
            {
                if (account.ProviderCode == ProviderCode.Imap)
                    accounts.Add(account);
            }
        }
    }

    /// <summary>Cuenta desconectada, borrada o que dejó de ser IMAP: se deja de escuchar.</summary>
    private async Task ReleaseInactiveAsync(HashSet<ActiveAccountRef> accounts)
    {
        var activeIds = accounts.Select(a => a.Id).ToHashSet();
        foreach (var (accountId, running) in _sessions.Where(s => !activeIds.Contains(s.Key)).ToList())
        {
            _sessions.Remove(accountId);
            await StopAsync(running);
        }
    }

    private async Task ClaimNewAsync(HashSet<ActiveAccountRef> accounts, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var max = Math.Max(0, options.Value.MaxAccountsPerInstance);
        foreach (var account in accounts)
        {
            if (_sessions.Count >= max)
                return;
            if (_sessions.ContainsKey(account.Id) || InCooldown(account.Id, now))
                continue;

            var lease = await distributedLock.AcquireAsync(LeaseKey(account.Id), LeaseTtl, ct);
            if (!lease.IsAcquired)
            {
                await lease.DisposeAsync();
                continue;
            }

            Start(account, lease);
        }
    }

    private bool InCooldown(Guid accountId, DateTimeOffset now)
    {
        if (!_cooldownUntil.TryGetValue(accountId, out var until))
            return false;
        if (now < until)
            return true;

        _cooldownUntil.Remove(accountId);
        return false;
    }

    private void Start(ActiveAccountRef account, ILockHandle lease)
    {
        var session = sessionFactory.Create(account);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        var task = Task.Run(() => session.RunAsync(cts.Token), CancellationToken.None);
        _sessions[account.Id] = new RunningSession(task, cts, lease);
    }

    private async Task StopAsync(RunningSession running)
    {
        await running.Cancellation.CancelAsync();
        try
        {
            await running.Task.WaitAsync(StopTimeout);
        }
        catch (Exception)
        {
            // La sesión ya terminó o no respondió a tiempo; igual se suelta el lease.
        }
        finally
        {
            await running.Lease.DisposeAsync();
            running.Cancellation.Dispose();
        }
    }

    private TimeSpan LeaseTtl => TimeSpan.FromSeconds(Math.Max(15, options.Value.LeaseSeconds));

    public static string LeaseKey(Guid accountId) => $"connectors:listener:lease:{accountId:N}";

    private sealed record RunningSession(Task Task, CancellationTokenSource Cancellation, ILockHandle Lease);
}
