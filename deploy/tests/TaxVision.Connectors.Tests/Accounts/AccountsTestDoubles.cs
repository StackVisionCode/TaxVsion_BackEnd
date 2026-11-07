using BuildingBlocks.Results;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Domain.Accounts;

namespace TaxVision.Connectors.Tests.Accounts;

internal sealed class FakeSmtpCredentialsRepository : ISmtpCredentialsRepository
{
    public List<SmtpCredentials> Credentials { get; } = [];

    public Task AddAsync(SmtpCredentials credentials, CancellationToken ct = default)
    {
        Credentials.Add(credentials);
        return Task.CompletedTask;
    }

    public Task<Result<SmtpCredentials>> GetByAccountIdAsync(Guid accountId, CancellationToken ct = default)
    {
        var found = Credentials.Find(c => c.AccountId == accountId);
        return Task.FromResult(
            found is null
                ? Result.Failure<SmtpCredentials>(new Error("SmtpCredentials.NotFound", "Not found."))
                : Result.Success(found)
        );
    }

    public void Remove(SmtpCredentials credentials) => Credentials.Remove(credentials);
}

/// <summary>Éxito por defecto — los tests que quieren ver un rechazo por credenciales malas setean ImapResult/SmtpResult.</summary>
/// <summary>Unidad del handler que sabe si está abierta: para ver qué corre fuera de la transacción (CON-12).</summary>
internal sealed class TrackingOutboxUnit(BuildingBlocks.Persistence.IUnitOfWork unitOfWork, Wolverine.IMessageBus bus)
    : TaxVision.Connectors.Application.Abstractions.IOutboxUnit
{
    public bool IsOpen { get; private set; }
    public bool Committed { get; private set; }

    public async Task RunAsync(
        Func<Wolverine.IMessageBus, CancellationToken, Task<bool>> work,
        CancellationToken ct = default
    )
    {
        IsOpen = true;
        try
        {
            Committed = await work(bus, ct);
            if (Committed)
                await unitOfWork.SaveChangesAsync(ct);
        }
        finally
        {
            IsOpen = false;
        }
    }
}

internal sealed class FakeManualAccountConnectivityValidator : IManualAccountConnectivityValidator
{
    public Result ImapResult { get; set; } = Result.Success();
    public Result SmtpResult { get; set; } = Result.Success();

    /// <summary>Corre en cada prueba de conexión, para mirar el estado de ese momento.</summary>
    public Action? OnValidate { get; set; }
    public List<string> ValidatedImapHosts { get; } = [];
    public List<string> ValidatedSmtpHosts { get; } = [];

    public Task<Result> ValidateImapAsync(
        string host,
        int port,
        bool useSsl,
        string username,
        string password,
        CancellationToken ct = default
    )
    {
        OnValidate?.Invoke();
        ValidatedImapHosts.Add(host);
        return Task.FromResult(ImapResult);
    }

    public Task<Result> ValidateSmtpAsync(
        string host,
        int port,
        bool useStartTls,
        string username,
        string password,
        CancellationToken ct = default
    )
    {
        ValidatedSmtpHosts.Add(host);
        return Task.FromResult(SmtpResult);
    }
}
