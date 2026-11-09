using TaxVision.BuildingBlocks.Testing.Architecture;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Architecture;

/// <summary>
/// NOT-22 sobre todos los servicios: nadie recibe el bus por constructor si un handler lo usa. Lo legítimo y la deuda
/// quedan listados con su motivo. Al corregir la deuda, la entrada se retira.
/// </summary>
public sealed class ConstructorBusTests
{
    private const string SourceDirectory = "src/Services";

    private const string Services = SourceDirectory + "/";
    private const string JobOnly = "Solo lo resuelve un job: no hay handler con el que compartir outbox";
    private const string HttpOnly = "Filtro de MVC: corre en la petición, sin handler";
    private const string BuiltByHandler =
        "Lo arma el handler con el bus que recibe por parámetro, que es su contexto; no sale del contenedor";
    private const string BuiltByHandlerMeasured = BuiltByHandler + " (BB-17: medido en lo entregado)";

    private static readonly IReadOnlyDictionary<string, string> Known = new Dictionary<string, string>(
        StringComparer.Ordinal
    )
    {
        [Services + "Auth/Application/Offboarding/OffboardingRunSweeper.cs OffboardingRunSweeper"] = JobOnly,
        [Services + "Auth/Onboarding/Application/Sagas/Services/OnboardingRetryProcessor.cs OnboardingRetryProcessor"] =
            JobOnly,
        [
            Services
                + "Auth/Onboarding/Application/TenantOnboardings/Services/OnboardingOrphanDetectorProcessor.cs OnboardingOrphanDetectorProcessor"
        ] = JobOnly,
        [
            Services
                + "Billing/TaxVision.Billing.Application/Invoices/IntegrationEvents/InvoicePaymentSucceededConsumer.cs Rejection"
        ] = BuiltByHandlerMeasured,
        [Services + "Connectors/TaxVision.Connectors.Application/Abstractions/IOutboxUnit.cs HandlerTransactionUnit"] =
            BuiltByHandler,
        [
            Services
                + "Customer/TaxVision.Customer.Application/Customers/Consumers/CustomerCreationFromFormRequestedConsumer.cs CreationContext"
        ] = BuiltByHandlerMeasured,
        [Services + "Notification/TaxVision.Notification.Infrastructure/Messaging/HandlerBus.cs HandlerBus"] =
            "NOT-22: es la pieza que devuelve el bus del handler; el del constructor es el respaldo sin handler",
        [
            Services
                + "Postmaster/TaxVision.Postmaster.Application/Feedback/DeliveryFeedbackStores.cs DeliveryFeedbackStores"
        ] = BuiltByHandler,
        [Services + "Postmaster/TaxVision.Postmaster.Application/Sending/Lanes/LanePreparation.cs LanePreparation"] =
            BuiltByHandler,
        [Services + "Reminder/TaxVision.Reminder.Infrastructure/Scheduling/ReminderFireJob.cs ReminderFireJob"] =
            JobOnly,
        [Services + "Scribe/TaxVision.Scribe.Infrastructure/Storage/CloudStorageClient.cs CloudStorageClient"] =
            "SCR-07: los seeders no tienen handler y el archivo debe existir antes de la fila; excepción de BB-3",
        [Services + "Tasks/TaxVision.Tasks.Api/Filters/FormSessionFilter.cs FormSessionFilter"] = HttpOnly,
        [
            Services
                + "Tasks/TaxVision.Tasks.ClientForms.Application/Requests/Consumers/ExternalParticipantNoticeConsumer.cs Senders"
        ] = BuiltByHandlerMeasured,
    };

    [Fact]
    public void NoCollaboratorTakesTheBusByConstructor()
    {
        var offenders = ConstructorBusRules
            .Scan(SourceDirectory)
            .Where(finding => !Known.ContainsKey(finding.Key))
            .Select(finding => finding.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Reciben el bus por constructor ({offenders.Length}). Si lo usa un handler, pasarle su bus por parámetro:\n  "
                + string.Join("\n  ", offenders)
        );
    }

    [Fact]
    public void Known_EntriesAreStillNeeded()
    {
        var found = ConstructorBusRules.Scan(SourceDirectory).Select(finding => finding.Key).ToHashSet();
        var stale = Known.Keys.Where(key => !found.Contains(key)).Order(StringComparer.Ordinal).ToArray();

        Assert.True(stale.Length == 0, "Entradas que ya no hacen falta:\n  " + string.Join("\n  ", stale));
    }

    [Fact]
    public void Classify_FindsPrimaryAndExplicitConstructors()
    {
        const string source = """
            public sealed class Primary(IClock clock, IMessageBus bus) { }
            public sealed class Classic
            {
                public Classic(IMessageContext context) { }
            }
            """;

        Assert.Equal(["Primary", "Classic"], ConstructorBusRules.Classify(source).Select(f => f.TypeName));
    }

    [Fact]
    public void Classify_IgnoresAMethodParameter()
    {
        const string source = """
            public static class Handler
            {
                public static Task Handle(Command command, IMessageBus bus) => Task.CompletedTask;
            }
            """;

        Assert.Empty(ConstructorBusRules.Classify(source));
    }
}
