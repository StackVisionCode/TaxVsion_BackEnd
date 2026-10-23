using System.Reflection;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class ClientFormsIntegrationContractTests
{
    private static readonly Type[] Facts =
    [
        typeof(FormRequestAssignedIntegrationEvent),
        typeof(FormRequestSubmittedIntegrationEvent),
        typeof(FormRequestReturnedIntegrationEvent),
        typeof(FormRequestCompletedIntegrationEvent),
        typeof(FormRequestCancelledIntegrationEvent),
        typeof(FormAttachmentRejectedIntegrationEvent),
        typeof(FormRequestReminderDueIntegrationEvent),
        typeof(FormActionFailedIntegrationEvent),
    ];

    private static readonly IReadOnlyDictionary<Type, string[]> AllowedProperties = new Dictionary<Type, string[]>
    {
        [typeof(FormRequestAssignedIntegrationEvent)] = ["FormRequestId", "Participant", "Title", "HasMessage", "DueAtUtc", "TaxYear", "RequestedByUserId", "FormTemplateId", "VersionNumber"],
        [typeof(FormRequestSubmittedIntegrationEvent)] = ["FormRequestId", "Participant", "ResponsibleUserId", "CustomerDisplayName", "Title", "RevisionNumber", "IsResubmission", "RequestedByUserId", "SubmittedBy"],
        [typeof(FormRequestReturnedIntegrationEvent)] = ["FormRequestId", "Participant", "Title", "RevisionNumber", "FieldCommentCount", "ReturnedByUserId", "DueAtUtc"],
        [typeof(FormRequestCompletedIntegrationEvent)] = ["FormRequestId", "Participant", "Title", "RevisionNumber", "CompletedByUserId"],
        [typeof(FormRequestCancelledIntegrationEvent)] = ["FormRequestId", "Participant", "Title", "Reason", "CancelledByUserId", "NotifyClient"],
        [typeof(FormAttachmentRejectedIntegrationEvent)] = ["FormRequestId", "Participant", "ResponsibleUserId", "CustomerDisplayName", "Title", "AttachmentId", "FileId", "FileExtension", "ReasonCode", "RequestedByUserId"],
        [typeof(FormRequestReminderDueIntegrationEvent)] = ["FormRequestId", "Participant", "Title", "DueAtUtc", "IsOverdue", "ReminderNumber", "IsManual", "SentByUserId"],
        [typeof(FormActionFailedIntegrationEvent)] = ["FormRequestId", "ActionType", "ErrorCode", "ResponsibleUserId"],
    };

    [Fact]
    public void Every_fact_inherits_the_standard_envelope_with_correlation_id()
    {
        Assert.All(Facts, type =>
        {
            Assert.True(typeof(IntegrationEvent).IsAssignableFrom(type));
            Assert.NotNull(type.GetProperty(nameof(IntegrationEvent.CorrelationId)));
        });
    }

    [Fact]
    public void Every_fact_has_an_explicit_safe_property_allow_list()
    {
        foreach (var type in Facts)
        {
            var actual = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(property => property.Name)
                .Order()
                .ToArray();
            Assert.Equal(AllowedProperties[type].Order(), actual);
        }
    }

    [Fact]
    public void Every_fact_is_routed_to_the_standard_fanout()
    {
        var program = File.ReadAllText(Path.Combine(Root(), "src", "Services", "Tasks", "TaxVision.Tasks.Api", "Program.cs"));
        Assert.All(Facts, type => Assert.Contains(
            $"PublishMessage<{type.Name}>().ToRabbitExchange(\"taxvision-events\")",
            program,
            StringComparison.Ordinal
        ));
    }

    [Fact]
    public void Every_fact_has_a_notification_consumer()
    {
        var directory = Path.Combine(
            Root(),
            "src", "Services", "Notification", "TaxVision.Notification.Application", "Consumers", "ClientForms"
        );
        var source = string.Join('\n', Directory.GetFiles(directory, "*.cs").Select(File.ReadAllText));
        Assert.All(Facts, type =>
        {
            Assert.Contains(type.Name, source, StringComparison.Ordinal);
            Assert.Contains("Handle(", source, StringComparison.Ordinal);
        });
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TaxVision.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
