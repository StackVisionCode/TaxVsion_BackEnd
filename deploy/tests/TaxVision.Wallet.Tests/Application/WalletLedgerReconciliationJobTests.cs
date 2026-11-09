using Microsoft.Extensions.Logging;
using TaxVision.Wallet.Application.Wallet.Abstractions;
using TaxVision.Wallet.Infrastructure.Scheduling;
using TaxVision.Wallet.Tests.TestDoubles;

namespace TaxVision.Wallet.Tests.Application;

/// <summary>El job solo avisa: error por descuadre, advertencia si el tenant no se puede conciliar.</summary>
public sealed class WalletLedgerReconciliationJobTests
{
    private readonly FakeWalletRepository _wallets = new();
    private readonly RecordingLogger _logger = new();

    [Fact]
    public async Task Report_NoMismatches_LogsNothing()
    {
        var reported = await WalletLedgerReconciliationJob.ReportAsync(_wallets, _logger, CancellationToken.None);

        Assert.Equal(0, reported);
        Assert.Empty(_logger.Levels);
    }

    [Fact]
    public async Task Report_OneEntryPerTenant_ErrorOrWarningByKind()
    {
        _wallets.BalanceMismatches.Add(new WalletBalanceMismatch(Guid.NewGuid(), 1, 100, 0, 90, 0));
        _wallets.BalanceMismatches.Add(new WalletBalanceMismatch(Guid.NewGuid(), 2, 100, 0, 100, 0));

        var reported = await WalletLedgerReconciliationJob.ReportAsync(_wallets, _logger, CancellationToken.None);

        Assert.Equal(2, reported);
        Assert.Equal([LogLevel.Error, LogLevel.Warning], _logger.Levels);
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => Levels.Add(logLevel);
    }
}
