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
}

/// <summary>Éxito por defecto — los tests que quieren ver un rechazo por credenciales malas setean ImapResult/SmtpResult.</summary>
internal sealed class FakeManualAccountConnectivityValidator : IManualAccountConnectivityValidator
{
    public Result ImapResult { get; set; } = Result.Success();
    public Result SmtpResult { get; set; } = Result.Success();
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
