using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BuildingBlocks.Messaging.BillingIntegrationEvents;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.CorrespondenceIntegrationEvents;
using BuildingBlocks.Messaging.NotesIntegrationEvents;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Messaging.SmsIntegrationEvents;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using Wolverine.Attributes;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Messaging;

/// <summary>
/// Campos de actor que agrega el Work Profile: opcionales (nullable, sin <c>required</c>) para que los
/// publicadores y consumidores actuales no cambien, y con la identidad de cable intacta.
/// </summary>
public sealed class WorkProfileEventContractTests
{
    public static TheoryData<Type, string> OptionalFields =>
        new()
        {
            { typeof(TaskCreatedIntegrationEvent), "CreatedByUserId" },
            { typeof(TaskDueChangedIntegrationEvent), "ChangedByUserId" },
            { typeof(TaskAttachmentDetachedIntegrationEvent), "DetachedByUserId" },
            { typeof(InvoiceIssuedIntegrationEvent), "IssuedByUserId" },
            { typeof(InvoicePaidIntegrationEvent), "RecordedByUserId" },
            { typeof(InvoiceVoidedIntegrationEvent), "VoidedByUserId" },
            { typeof(InvoiceVoidedIntegrationEvent), "CustomerId" },
            { typeof(InvoiceSentToCustomerIntegrationEvent), "SentByUserId" },
            { typeof(SignatureRequestSentIntegrationEvent), "SentByUserId" },
            { typeof(SignatureRequestSentIntegrationEvent), "CustomerId" },
            { typeof(SignatureRequestCreatedIntegrationEvent), "CustomerId" },
            { typeof(SignatureRequestCanceledIntegrationEvent), "CustomerId" },
            { typeof(SignatureRequestCompletedIntegrationEvent), "CustomerId" },
            { typeof(PreparerSignedIntegrationEvent), "CustomerId" },
            { typeof(AppointmentRescheduledIntegrationEvent), "ChangedByUserId" },
            { typeof(AppointmentCancelledIntegrationEvent), "ChangedByUserId" },
            { typeof(CustomerDocumentUploadedIntegrationEvent), "UploadedByUserId" },
            { typeof(CustomerDocumentUploadedIntegrationEvent), "UploadedByActorType" },
            { typeof(NoteCreatedIntegrationEvent), "CustomerId" },
            { typeof(NoteUpdatedIntegrationEvent), "ActorUserId" },
            { typeof(NoteDeletedIntegrationEvent), "ActorUserId" },
            { typeof(CorrespondenceEmailSentIntegrationEvent), "CustomerId" },
        };

    [Theory]
    [MemberData(nameof(OptionalFields))]
    public void New_field_is_optional(Type eventType, string property)
    {
        var info = eventType.GetProperty(property, BindingFlags.Public | BindingFlags.Instance);

        Assert.True(info is not null, $"{eventType.Name}.{property} no existe.");
        Assert.True(info!.GetCustomAttribute<RequiredMemberAttribute>() is null, $"{property} no puede ser required.");
        Assert.True(info.SetMethod?.ReturnParameter.GetRequiredCustomModifiers().Length > 0, $"{property} es init.");
        var nullable =
            Nullable.GetUnderlyingType(info.PropertyType) is not null
            || new NullabilityInfoContext().Create(info).WriteState == NullabilityState.Nullable;
        Assert.True(nullable, $"{eventType.Name}.{property} tiene que ser nullable.");
    }

    [Theory]
    [InlineData(typeof(SmsMessageSentIntegrationEvent), "sms.message.sent.v1")]
    [InlineData(typeof(CorrespondenceEmailSentIntegrationEvent), "correspondence.email.sent.v1")]
    [InlineData(
        typeof(TaskCreatedIntegrationEvent),
        "BuildingBlocks.Messaging.TasksIntegrationEvents.TaskCreatedIntegrationEvent"
    )]
    [InlineData(
        typeof(InvoiceIssuedIntegrationEvent),
        "BuildingBlocks.Messaging.BillingIntegrationEvents.InvoiceIssuedIntegrationEvent"
    )]
    [InlineData(typeof(CustomerDocumentUploadedIntegrationEvent), "cloudstorage.customer_document_uploaded.v1")]
    public void Wire_identity_is_the_approved_one(Type eventType, string identity)
    {
        Assert.Equal(identity, eventType.GetCustomAttribute<MessageIdentityAttribute>()?.Alias);
    }

    [Fact]
    public void Sms_message_sent_carries_message_customer_and_actor()
    {
        var sent = new SmsMessageSentIntegrationEvent
        {
            MessageId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            SentByUserId = Guid.NewGuid(),
        };

        Assert.NotEqual(Guid.Empty, sent.MessageId);
        Assert.NotEqual(default, sent.OccurredOn);
    }

    [Fact]
    public void Correspondence_email_sent_without_conversation_has_no_thread()
    {
        var sent = new CorrespondenceEmailSentIntegrationEvent
        {
            DraftId = Guid.NewGuid(),
            SentByUserId = Guid.NewGuid(),
        };

        Assert.Null(sent.CustomerId);
        Assert.Null(sent.ThreadId);
    }

    [Fact]
    public void An_old_payload_without_the_new_fields_still_deserializes()
    {
        var old = """{"TaskId":"6f1c2c0e-58a1-4c55-9a55-3f0a2f4c3c11","Title":"T","Priority":"Normal"}""";

        var parsed = JsonSerializer.Deserialize<TaskCreatedIntegrationEvent>(old);

        Assert.NotNull(parsed);
        Assert.Null(parsed!.CreatedByUserId);
    }
}
