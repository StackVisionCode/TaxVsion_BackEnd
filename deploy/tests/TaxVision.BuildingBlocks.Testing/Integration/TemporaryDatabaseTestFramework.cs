using System.Reflection;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace TaxVision.BuildingBlocks.Testing.Integration;

/// <summary>
/// BB-10. El framework de siempre, más borrar las bases temporales antes de cerrar la corrida. Se activa por proyecto:
/// <c>[assembly: TestFramework(TemporaryDatabaseTestFramework.TypeName, TemporaryDatabaseTestFramework.AssemblyName)]</c>.
/// </summary>
public sealed class TemporaryDatabaseTestFramework(IMessageSink messageSink) : XunitTestFramework(messageSink)
{
    public const string TypeName = "TaxVision.BuildingBlocks.Testing.Integration.TemporaryDatabaseTestFramework";
    public const string AssemblyName = "TaxVision.BuildingBlocks.Testing";

    protected override ITestFrameworkExecutor CreateExecutor(AssemblyName assemblyName) =>
        new Executor(assemblyName, SourceInformationProvider, DiagnosticMessageSink);

    private sealed class Executor(
        AssemblyName assemblyName,
        ISourceInformationProvider sourceInformationProvider,
        IMessageSink diagnosticMessageSink
    ) : XunitTestFrameworkExecutor(assemblyName, sourceInformationProvider, diagnosticMessageSink)
    {
        protected override async void RunTestCases(
            IEnumerable<IXunitTestCase> testCases,
            IMessageSink executionMessageSink,
            ITestFrameworkExecutionOptions executionOptions
        )
        {
            using var runner = new Runner(
                TestAssembly,
                testCases,
                DiagnosticMessageSink,
                executionMessageSink,
                executionOptions
            );
            await runner.RunAsync();
        }
    }

    private sealed class Runner(
        ITestAssembly testAssembly,
        IEnumerable<IXunitTestCase> testCases,
        IMessageSink diagnosticMessageSink,
        IMessageSink executionMessageSink,
        ITestFrameworkExecutionOptions executionOptions
    ) : XunitTestAssemblyRunner(testAssembly, testCases, diagnosticMessageSink, executionMessageSink, executionOptions)
    {
        protected override async Task BeforeTestAssemblyFinishedAsync()
        {
            try
            {
                TemporaryTestDatabase.DropCreated();
            }
            catch (Exception failure)
            {
                // La base que quede la borra la siguiente corrida: no es motivo para tumbar esta.
                DiagnosticMessageSink.OnMessage(
                    new DiagnosticMessage($"Temporary databases not dropped: {failure.Message}")
                );
            }

            await base.BeforeTestAssemblyFinishedAsync();
        }
    }
}
