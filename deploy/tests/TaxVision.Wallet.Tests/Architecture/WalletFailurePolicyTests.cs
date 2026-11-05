using System.Reflection;
using BuildingBlocks.Messaging;
using TaxVision.Wallet.Api;
using TaxVision.Wallet.Application.Wallet.Consumers;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.Runtime.Handlers;

namespace TaxVision.Wallet.Tests.Architecture;

/// <summary>Las reglas de fallo de Wallet, evaluadas como lo hace Wolverine (la primera que aplica gana).</summary>
public sealed class WalletFailurePolicyTests
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [Fact]
    public void WalletTopUpNotFound_IsRegisteredBeforeStandardPolicies()
    {
        var continuation = FirstAttemptContinuation(
            new WalletTopUpNotFoundException(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())
        );

        Assert.Equal("MoveToErrorQueue", continuation.GetType().Name);
    }

    [Fact]
    public void OtherExceptions_KeepTheStandardRetry()
    {
        var continuation = FirstAttemptContinuation(new InvalidOperationException("down"));

        Assert.Equal("RetryInlineContinuation", continuation.GetType().Name);
    }

    [Fact]
    public void ProgramCs_AppliesWalletRulesBeforeStandardPolicies()
    {
        var program = File.ReadAllText(ProgramPath());
        var wallet = program.IndexOf(nameof(WalletFailurePolicies.ApplyWalletFailureRules), StringComparison.Ordinal);
        var standard = program.IndexOf(
            nameof(WolverineFailurePolicies.ApplyStandardFailurePolicies),
            StringComparison.Ordinal
        );

        Assert.True(wallet >= 0 && standard > wallet, "Program.cs debe llamar ApplyWalletFailureRules antes.");
    }

    // Mismo orden que Program.cs. HandlerGraph y DetermineExecutionContinuation son internos en Wolverine 6.14.
    private static object FirstAttemptContinuation(Exception exception)
    {
        var options = new WolverineOptions().ApplyWalletFailureRules().ApplyStandardFailurePolicies();
        var graph = (HandlerGraph)typeof(WolverineOptions).GetProperty("HandlerGraph", Any)!.GetValue(options)!;
        return typeof(FailureRuleCollection)
            .GetMethod("DetermineExecutionContinuation", Any)!
            .Invoke(graph.Failures, [exception, new Envelope { Attempts = 1 }])!;
    }

    private static string ProgramPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "Services", "Wallet", "TaxVision.Wallet.Api", "Program.cs");
    }
}
