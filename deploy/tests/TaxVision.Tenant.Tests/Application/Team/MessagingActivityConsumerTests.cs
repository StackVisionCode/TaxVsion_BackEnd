using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.CorrespondenceIntegrationEvents;
using BuildingBlocks.Messaging.SmsIntegrationEvents;
using TaxVision.Tenant.Application.Team.Consumers;
using TaxVision.Tenant.Domain.Team;
using TaxVision.Tenant.Tests.TestSupport;

namespace TaxVision.Tenant.Tests.Application.Team;

public sealed class SmsActivityConsumerTests : ActivityConsumerContract
{
    private static readonly Guid Actor = Guid.NewGuid();

    protected override IEnumerable<ActivityCase> Cases()
    {
        var messageId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var sent = Stamp(
            new SmsMessageSentIntegrationEvent
            {
                MessageId = messageId,
                CustomerId = customerId,
                SentByUserId = Actor,
            }
        );
        yield return new(
            sent,
            sent with
            {
                SentByUserId = Guid.Empty,
            },
            (e, r, u, c) => SmsActivityConsumer.Handle((SmsMessageSentIntegrationEvent)e, r, u, c, default),
            Actor,
            ActivityVerbs.SmsSent,
            messageId,
            customerId,
            ActivityVisibility.Team
        );
    }
}

public sealed class CorrespondenceActivityConsumerTests : ActivityConsumerContract
{
    private static readonly Guid Actor = Guid.NewGuid();

    // Respuesta: el recurso es el hilo. Correo nuevo: aún no hay hilo, el recurso es el mensaje.
    protected override IEnumerable<ActivityCase> Cases()
    {
        var threadId = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var reply = Stamp(
            new CorrespondenceEmailSentIntegrationEvent
            {
                DraftId = Guid.NewGuid(),
                ThreadId = threadId,
                CustomerId = null,
                SentByUserId = Actor,
            }
        );
        var fresh = Stamp(
            new CorrespondenceEmailSentIntegrationEvent
            {
                DraftId = draftId,
                CustomerId = customerId,
                SentByUserId = Actor,
            }
        );
        yield return new(
            reply,
            reply with
            {
                SentByUserId = Guid.Empty,
            },
            Handle,
            Actor,
            ActivityVerbs.EmailReplied,
            threadId,
            null,
            ActivityVisibility.Team
        );
        yield return new(
            fresh,
            fresh with
            {
                SentByUserId = Guid.Empty,
            },
            Handle,
            Actor,
            ActivityVerbs.EmailSent,
            draftId,
            customerId,
            ActivityVisibility.Team
        );
    }

    private static Task Handle(
        BuildingBlocks.Messaging.IntegrationEvent e,
        TaxVision.Tenant.Application.Team.Abstractions.IActivityEntryRepository r,
        BuildingBlocks.Persistence.IUnitOfWork u,
        BuildingBlocks.Common.ICorrelationContext c
    ) => CorrespondenceActivityConsumer.Handle((CorrespondenceEmailSentIntegrationEvent)e, r, u, c, default);
}

public sealed class CloudStorageActivityConsumerTests : ActivityConsumerContract
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid FileId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();

    private static CustomerDocumentUploadedIntegrationEvent Uploaded(string? actorType) =>
        Stamp(
            new CustomerDocumentUploadedIntegrationEvent
            {
                CustomerId = CustomerId,
                FileId = FileId,
                UploadedByUserId = Actor,
                UploadedByActorType = actorType,
            }
        );

    protected override IEnumerable<ActivityCase> Cases()
    {
        var uploaded = Uploaded("TenantEmployee");
        yield return new(
            uploaded,
            uploaded with
            {
                UploadedByUserId = null,
            },
            (e, r, u, c) =>
                CloudStorageActivityConsumer.Handle((CustomerDocumentUploadedIntegrationEvent)e, r, u, c, default),
            Actor,
            ActivityVerbs.DocumentUploaded,
            FileId,
            CustomerId,
            ActivityVisibility.Team
        );
    }

    // Solo staff de la oficina: el cliente del portal, un servicio o un tipo ausente no son actividad del equipo.
    [Theory]
    [InlineData("TenantAdmin", true)]
    [InlineData("TenantEmployee", true)]
    [InlineData("CustomerPortal", false)]
    [InlineData("Service", false)]
    [InlineData("PlatformAdmin", false)]
    [InlineData(null, false)]
    public async Task Only_uploads_by_office_staff_are_recorded(string? actorType, bool recorded)
    {
        var harness = new ActivityHarness();

        await CloudStorageActivityConsumer.Handle(Uploaded(actorType), harness, harness, harness, default);

        Assert.Equal(recorded, harness.Saved.Count == 1);
    }
}
